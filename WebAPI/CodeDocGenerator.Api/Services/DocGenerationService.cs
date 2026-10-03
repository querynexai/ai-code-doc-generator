using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeDocGenerator.Api.Models;

namespace CodeDocGenerator.Api.Services;

public class DocGenerationService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _bulkModel;

    // Serialized rate limiter — Groq free tier is 8K TPM
    private readonly SemaphoreSlim _rateLock = new(1, 1);
    private DateTime _windowStart = DateTime.UtcNow;
    private int _windowTokenEstimate = 0;
    private const int MaxTokensPerMinute = 7000; // headroom under 8000

    public DocGenerationService(HttpClient http, IConfiguration config)
    {
        _http = http;
        var apiKey = config["Groq:ApiKey"]
            ?? throw new InvalidOperationException("Groq:ApiKey not set");

        _model = config["Groq:Model"] ?? "openai/gpt-oss-120b";
        _bulkModel = config["Groq:BulkModel"] ?? _model;

        _http.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ---------- PUBLIC API ----------

    public async Task<(string? Xml, decimal? Confidence)> GenerateXmlDocAsync(
        RoslynAnalysisService.MemberInfo member)
    {
        var systemPrompt =
            "You are a C# documentation writer. Output ONLY an XML doc comment (starting with ///). " +
            "No markdown, no prose. Use <summary>, and <param>/<returns>/<exception> only when relevant. " +
            "Keep summary under 120 chars. Be specific to the member's purpose.";

        var userMessage =
            $"Type: {member.MemberType}\n" +
            $"Name: {member.MemberName}\n" +
            $"FQN: {member.FullyQualifiedName ?? "unknown"}\n" +
            $"Existing XML (may be empty): {Truncate(member.OriginalXml, 200)}";

        var payload = new
        {
            model = _bulkModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.2,
            max_tokens = 300
        };

        var xml = await SendWithRetryAsync(payload);
        xml = xml.Replace("```xml", "").Replace("```", "").Trim();

        decimal confidence = 0.7m;
        if (xml.Contains("<summary>")) confidence += 0.15m;
        if (xml.Length > 80) confidence += 0.1m;
        if (xml.Contains("<param") || xml.Contains("<returns>")) confidence += 0.05m;
        if (confidence > 1m) confidence = 1m;

        return (xml, confidence);
    }

    public async Task<string> GenerateReadmeAsync(
        GenerateReadmeRequest request, RoslynAnalysisService.ProjectInfo info)
    {
        var systemPrompt =
            "You are a technical writer. Generate a professional README.md for a .NET project. " +
            "Output ONLY Markdown. Sections: # Title, short description, ## Features (bullets), " +
            "## Tech Stack, ## Getting Started, ## Project Structure. Be concise.";

        var userMessage =
            $"Project: {request.ProjectName ?? info.ProjectName}\n" +
            $"Description: {request.Description ?? "A .NET project."}\n" +
            $"Tech Stack: {request.TechStack ?? "C# / .NET"}\n" +
            $"Files: {info.TotalFiles}, Classes: {info.TotalClasses}, " +
            $"Methods: {info.TotalMethods}";

        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.3,
            max_tokens = 1200
        };

        return await SendWithRetryAsync(payload);
    }

    // ---------- RATE LIMITING + RETRY ----------

    private async Task<string> SendWithRetryAsync(object payload)
    {
        await WaitForBudgetAsync();

        const int maxAttempts = 5;
        int attempt = 0;
        int delayMs = 2000;

        while (true)
        {
            attempt++;
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("chat/completions", content);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var text = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? string.Empty;

                    TrackUsage(body);
                    return text;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= maxAttempts)
                        throw new Exception($"Groq 429 after {maxAttempts} attempts: {body}");

                    var retryAfter = ParseRetryAfter(body) ?? delayMs;
                    Console.WriteLine($"[Groq] Rate limited. Retry in {retryAfter}ms (attempt {attempt}/{maxAttempts})");
                    await Task.Delay(retryAfter);
                    delayMs = Math.Min(delayMs * 2, 30000);
                    continue;
                }

                throw new Exception($"Groq error ({(int)response.StatusCode}): {body}");
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= maxAttempts) throw;
                Console.WriteLine($"[Groq] Network error, retrying: {ex.Message}");
                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 30000);
            }
        }
    }

    private async Task WaitForBudgetAsync()
    {
        await _rateLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _windowStart).TotalSeconds >= 60)
            {
                _windowStart = now;
                _windowTokenEstimate = 0;
            }

            if (_windowTokenEstimate >= MaxTokensPerMinute)
            {
                var waitMs = 60000 - (int)(now - _windowStart).TotalMilliseconds + 500;
                if (waitMs > 0)
                {
                    Console.WriteLine($"[Groq] Token budget near limit, waiting {waitMs}ms");
                    await Task.Delay(waitMs);
                }
                _windowStart = DateTime.UtcNow;
                _windowTokenEstimate = 0;
            }

            _windowTokenEstimate += 500;
        }
        finally
        {
            _rateLock.Release();
        }

        await Task.Delay(300);
    }

    private void TrackUsage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var total))
            {
                _windowTokenEstimate += total.GetInt32() - 500;
            }
        }
        catch { /* non-fatal */ }
    }

    private static int? ParseRetryAfter(string body)
    {
        try
        {
            var idx = body.IndexOf("try again in", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var sub = body.Substring(idx + 12).Trim();
            var end = sub.IndexOf('s');
            if (end < 0) return null;

            if (double.TryParse(sub.Substring(0, end), out var seconds))
                return (int)(seconds * 1000) + 500;
        }
        catch { }
        return null;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(none)";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}