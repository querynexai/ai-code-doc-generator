namespace CodeDocGenerator.Api.Models;

public record AnalyzeProjectRequest(
    string ProjectPath,
    bool GenerateReadme = true,
    bool GenerateXmlDocs = true,
    int MaxMembers = 50);

public record ProjectSummaryDto(
    int ProjectId,
    string ProjectName,
    string? AssemblyName,
    string? TargetFramework,
    int TotalFiles,
    int TotalClasses,
    int TotalMethods,
    int TotalProperties,
    int DocumentedMembers,
    int UndocumentedMembers,
    string AnalysisStatus,
    int? AnalysisDurationMs,
    DateTime AnalyzedAt);

public record ProjectDetailDto(
    int ProjectId,
    string ProjectName,
    string? AssemblyName,
    string? TargetFramework,
    int TotalFiles,
    int TotalClasses,
    int TotalMethods,
    int TotalProperties,
    int DocumentedMembers,
    int UndocumentedMembers,
    string AnalysisStatus,
    int? AnalysisDurationMs,
    DateTime AnalyzedAt,
    List<GeneratedDocDto> Docs,
    string? ReadmeContent);

public record GeneratedDocDto(
    int DocId,
    string MemberType,
    string MemberName,
    string? FullyQualifiedName,
    string? FilePath,
    int? LineNumber,
    string? OriginalXml,
    string? GeneratedXml,
    decimal? AiConfidence,
    bool WasAccepted);

public record AnalysisResultDto(
    int ProjectId,
    bool Success,
    int TotalMembers,
    int DocumentedMembers,
    int UndocumentedMembers,
    int GeneratedCount,
    int SkippedCount,
    string? Error = null);

public record GenerateReadmeRequest(string? ProjectName, string? Description, string? TechStack);

public record RegenerateReadmeResponse(
    bool Success,
    int Length,
    string? Readme = null,
    string? Error = null);