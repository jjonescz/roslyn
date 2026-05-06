using System.Text.Json;
using LspVGrepTool.Models;

namespace LspVGrepTool.Reporting;

internal static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    public static string Render(ToolReport report)
    {
        var jsonReport = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            directory = report.Directory,
            repositoryCommitHash = report.RepositoryCommitHash,
            lscache = new
            {
                enabled = report.LsCacheEnabled,
                used = report.LsCacheUsed
            },
            roslynTarget = CreateRoslynTarget(report),
            queries = report.Queries.Select(query => new
            {
                type = query.Type,
                fields = query.Fields,
                algorithms = query.Algorithms.Select(algorithm => new
                {
                    name = algorithm.AlgorithmName,
                    outcome = algorithm.Outcome.ToString(),
                    summary = algorithm.Summary,
                    elapsedMilliseconds = algorithm.ElapsedTime.TotalMilliseconds,
                    characterCount = algorithm.CharacterCount,
                    lineCount = algorithm.LineCount
                })
            })
        };

        return JsonSerializer.Serialize(jsonReport, s_options);
    }

    private static object? CreateRoslynTarget(ToolReport report)
    {
        if (string.IsNullOrWhiteSpace(report.RoslynTargetPath) && string.IsNullOrWhiteSpace(report.RoslynTargetKind) && report.RoslynLoadTime is null)
            return null;

        return new
        {
            kind = report.RoslynTargetKind,
            path = report.RoslynTargetPath,
            loadTimeMilliseconds = report.RoslynLoadTime?.TotalMilliseconds
        };
    }
}
