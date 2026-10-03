using Microsoft.AspNetCore.Mvc;
using CodeDocGenerator.Api.Models;
using CodeDocGenerator.Api.Services;

namespace CodeDocGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly RoslynAnalysisService _roslyn;
    private readonly DocGenerationService _docGen;
    private readonly DocumentationService _docs;

    public ProjectsController(
        RoslynAnalysisService roslyn,
        DocGenerationService docGen,
        DocumentationService docs)
    {
        _roslyn = roslyn;
        _docGen = docGen;
        _docs = docs;
    }

    [HttpGet]
    public Task<List<ProjectSummaryDto>> List() => _docs.ListProjectsAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDetailDto>> Get(int id)
    {
        var result = await _docs.GetProjectAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<AnalysisResultDto>> Analyze([FromBody] AnalyzeProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return BadRequest(new AnalysisResultDto(0, false, 0, 0, 0, 0, 0, "Project path is required."));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var (info, members) = _roslyn.Analyze(request.ProjectPath);

            if (members.Count == 0)
                return BadRequest(new AnalysisResultDto(0, false, 0, 0, 0, 0, 0,
                    "No C# members found. Check that the path points to a folder with .cs files."));

            var projectId = await _docs.CreateProjectAsync(info);

            int generated = 0;
            int skipped = 0;

            if (request.GenerateXmlDocs)
            {
                // Skip members that already have XML docs — major rate-limit saver
                var toProcess = members
                    .Where(m => string.IsNullOrWhiteSpace(m.OriginalXml))
                    .Take(request.MaxMembers)
                    .ToList();

                skipped = members.Count - toProcess.Count;

                Console.WriteLine($"[Analyze] {toProcess.Count} members need docs, {skipped} skipped (already documented or over cap)");

                foreach (var member in toProcess)
                {
                    try
                    {
                        var (xml, confidence) = await _docGen.GenerateXmlDocAsync(member);
                        await _docs.SaveGeneratedDocAsync(projectId, member, xml, confidence);
                        if (!string.IsNullOrWhiteSpace(xml)) generated++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DocGen] Failed for {member.MemberName}: {ex.Message}");
                    }
                }
            }

            if (request.GenerateReadme)
            {
                try
                {
                    Console.WriteLine($"[Readme] Starting generation for project {projectId}");
                    var readme = await _docGen.GenerateReadmeAsync(
                        new GenerateReadmeRequest(info.ProjectName, null, null), info);
                    Console.WriteLine($"[Readme] Generated {readme.Length} characters");
                    await _docs.SaveReadmeAsync(projectId, readme);
                    Console.WriteLine($"[Readme] Saved successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Readme] FAILED: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[Readme] Stack: {ex.StackTrace}");
                }
            }

            sw.Stop();
            await _docs.UpdateProjectStatusAsync(projectId, "complete", (int)sw.ElapsedMilliseconds);

            return Ok(new AnalysisResultDto(
                projectId, true,
                members.Count,
                info.DocumentedMembers,
                info.UndocumentedMembers,
                generated,
                skipped));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(500, new AnalysisResultDto(0, false, 0, 0, 0, 0, 0, ex.Message));
        }
    }

    [HttpPost("{id}/readme")]
    public async Task<ActionResult<RegenerateReadmeResponse>> RegenerateReadme(int id)
    {
        try
        {
            var project = await _docs.GetProjectAsync(id);
            if (project is null)
                return NotFound(new RegenerateReadmeResponse(false, 0, null, "Project not found."));

            var info = new RoslynAnalysisService.ProjectInfo
            {
                ProjectName = project.ProjectName,
                AssemblyName = project.AssemblyName,
                TargetFramework = project.TargetFramework,
                TotalFiles = project.TotalFiles,
                TotalClasses = project.TotalClasses,
                TotalMethods = project.TotalMethods,
                TotalProperties = project.TotalProperties
            };

            var readme = await _docGen.GenerateReadmeAsync(
                new GenerateReadmeRequest(project.ProjectName, null, null), info);

            await _docs.SaveReadmeAsync(id, readme);

            return Ok(new RegenerateReadmeResponse(true, readme.Length, readme));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new RegenerateReadmeResponse(
                false, 0, null, $"{ex.GetType().Name}: {ex.Message}"));
        }
    }
}