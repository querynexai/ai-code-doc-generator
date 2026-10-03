using Npgsql;
using CodeDocGenerator.Api.Models;

namespace CodeDocGenerator.Api.Services;

public class DocumentationService
{
    private readonly NpgsqlDataSource _dataSource;

    public DocumentationService(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<int> CreateProjectAsync(RoslynAnalysisService.ProjectInfo info)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO analyzed_projects
              (project_name, assembly_name, target_framework,
               total_files, total_classes, total_methods, total_properties,
               documented_members, undocumented_members, analysis_status, ai_model)
              VALUES (@pn, @an, @tf, @tf2, @tc, @tm, @tp, @dm, @um, 'analyzing', 'groq/gpt-oss-120b')
              RETURNING project_id", conn);

        cmd.Parameters.AddWithValue("pn", info.ProjectName);
        cmd.Parameters.AddWithValue("an", (object?)info.AssemblyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tf", (object?)info.TargetFramework ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tf2", info.TotalFiles);
        cmd.Parameters.AddWithValue("tc", info.TotalClasses);
        cmd.Parameters.AddWithValue("tm", info.TotalMethods);
        cmd.Parameters.AddWithValue("tp", info.TotalProperties);
        cmd.Parameters.AddWithValue("dm", info.DocumentedMembers);
        cmd.Parameters.AddWithValue("um", info.UndocumentedMembers);

        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task SaveGeneratedDocAsync(int projectId, RoslynAnalysisService.MemberInfo member,
        string? generatedXml, decimal? confidence)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO generated_docs
              (project_id, member_type, member_name, fully_qualified_name,
               file_path, line_number, original_xml, generated_xml, ai_confidence)
              VALUES (@pid, @mt, @mn, @fqn, @fp, @ln, @ox, @gx, @ac)", conn);

        cmd.Parameters.AddWithValue("pid", projectId);
        cmd.Parameters.AddWithValue("mt", member.MemberType);
        cmd.Parameters.AddWithValue("mn", member.MemberName);
        cmd.Parameters.AddWithValue("fqn", (object?)member.FullyQualifiedName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fp", (object?)member.FilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ln", (object?)member.LineNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ox", (object?)member.OriginalXml ?? DBNull.Value);
        cmd.Parameters.AddWithValue("gx", (object?)generatedXml ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ac", (object?)confidence ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateProjectStatusAsync(int projectId, string status, int durationMs)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            @"UPDATE analyzed_projects
              SET analysis_status = @st, analysis_duration_ms = @dm
              WHERE project_id = @id", conn);

        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("dm", durationMs);
        cmd.Parameters.AddWithValue("id", projectId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveReadmeAsync(int projectId, string content)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO project_readmes (project_id, content) VALUES (@pid, @c)", conn);

        cmd.Parameters.AddWithValue("pid", projectId);
        cmd.Parameters.AddWithValue("c", content);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ProjectSummaryDto>> ListProjectsAsync()
    {
        var results = new List<ProjectSummaryDto>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT project_id, project_name, assembly_name, target_framework,
                     total_files, total_classes, total_methods, total_properties,
                     documented_members, undocumented_members, analysis_status,
                     analysis_duration_ms, analyzed_at
              FROM analyzed_projects ORDER BY analyzed_at DESC", conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ProjectSummaryDto(
                reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.GetDateTime(12)));
        }
        return results;
    }

    public async Task<ProjectDetailDto?> GetProjectAsync(int id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        ProjectSummaryDto? summary = null;
        await using (var cmd = new NpgsqlCommand(
            @"SELECT project_id, project_name, assembly_name, target_framework,
                     total_files, total_classes, total_methods, total_properties,
                     documented_members, undocumented_members, analysis_status,
                     analysis_duration_ms, analyzed_at
              FROM analyzed_projects WHERE project_id = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            summary = new ProjectSummaryDto(
                reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.GetDateTime(12));
        }

        var docs = new List<GeneratedDocDto>();
        await using (var cmd = new NpgsqlCommand(
            @"SELECT doc_id, member_type, member_name, fully_qualified_name,
                     file_path, line_number, original_xml, generated_xml, ai_confidence, was_accepted
              FROM generated_docs WHERE project_id = @id ORDER BY doc_id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                docs.Add(new GeneratedDocDto(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    reader.GetBoolean(9)));
            }
        }

        string? readme = null;
        await using (var cmd = new NpgsqlCommand(
            "SELECT content FROM project_readmes WHERE project_id = @id ORDER BY generated_at DESC LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            var result = await cmd.ExecuteScalarAsync();
            readme = result as string;
        }

        return new ProjectDetailDto(
            summary.ProjectId, summary.ProjectName, summary.AssemblyName, summary.TargetFramework,
            summary.TotalFiles, summary.TotalClasses, summary.TotalMethods, summary.TotalProperties,
            summary.DocumentedMembers, summary.UndocumentedMembers, summary.AnalysisStatus,
            summary.AnalysisDurationMs, summary.AnalyzedAt,
            docs, readme);
    }
}