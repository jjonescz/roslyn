using System.Text.Json;
using System.Text.Json.Serialization;

namespace LspVGrepTool.Models;

internal sealed class JsonSummaryReport
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    [JsonIgnore]
    public string SourcePath { get; private set; } = "";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("directory")]
    public string Directory { get; init; } = "";

    [JsonPropertyName("repositoryCommitHash")]
    public string? RepositoryCommitHash { get; init; }

    [JsonPropertyName("repositoryUrl")]
    public string? RepositoryUrl { get; set; }

    [JsonPropertyName("sourceLineCount")]
    public long? SourceLineCount { get; init; }

    [JsonPropertyName("lscache")]
    public JsonSummaryLsCache? LsCache { get; init; }

    [JsonPropertyName("roslynTarget")]
    public JsonSummaryRoslynTarget? RoslynTarget { get; init; }

    [JsonPropertyName("tgrepIndex")]
    public JsonSummaryTgrepIndex? TgrepIndex { get; init; }

    [JsonPropertyName("queries")]
    public List<JsonSummaryQuery> Queries { get; init; } = [];

    public static JsonSummaryReport FromToolReport(ToolReport report) =>
        new()
        {
            SchemaVersion = 1,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Directory = report.Directory,
            RepositoryCommitHash = report.RepositoryCommitHash,
            RepositoryUrl = report.RepositoryUrl,
            SourceLineCount = report.SourceLineCount,
            LsCache = new JsonSummaryLsCache
            {
                Enabled = report.LsCacheEnabled,
                Used = report.LsCacheUsed
            },
            RoslynTarget = CreateRoslynTarget(report),
            TgrepIndex = CreateTgrepIndex(report),
            Queries = report.Queries.Select(static query => new JsonSummaryQuery
            {
                Type = query.Type,
                Fields = query.Fields.ToDictionary(static field => field.Key, static field => field.Value, StringComparer.Ordinal),
                Algorithms = query.Algorithms.Select(static algorithm => new JsonSummaryAlgorithm
                {
                    Name = algorithm.AlgorithmName,
                    Outcome = algorithm.Outcome.ToString(),
                    Summary = algorithm.Summary,
                    ElapsedMilliseconds = algorithm.ElapsedTime.TotalMilliseconds,
                    CharacterCount = algorithm.CharacterCount,
                    LineCount = algorithm.LineCount
                }).ToList()
            }).ToList()
        };

    public static async Task<JsonSummaryReport> LoadAsync(string reportPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException($"JSON summary report was not found: '{reportPath}'.", reportPath);
        }

        await using var stream = File.OpenRead(reportPath);
        var report = await JsonSerializer.DeserializeAsync<JsonSummaryReport>(stream, s_options, cancellationToken);
        if (report is null)
        {
            throw new InvalidDataException($"JSON summary report '{reportPath}' did not contain a valid report.");
        }

        report.SourcePath = reportPath;
        return report;
    }

    private static JsonSummaryRoslynTarget? CreateRoslynTarget(ToolReport report)
    {
        if (string.IsNullOrWhiteSpace(report.RoslynTargetPath) && string.IsNullOrWhiteSpace(report.RoslynTargetKind) && report.RoslynLoadTime is null)
            return null;

        return new JsonSummaryRoslynTarget
        {
            Kind = report.RoslynTargetKind,
            Path = report.RoslynTargetPath,
            LoadTimeMilliseconds = report.RoslynLoadTime?.TotalMilliseconds,
            AttemptedProjectCount = report.RoslynAttemptedProjectCount,
            LoadedProjectCount = report.RoslynLoadedProjectCount,
            SkippedProjectCount = report.RoslynSkippedProjectCount,
            IsPartial = report.RoslynSkippedProjectCount > 0
        };
    }

    private static JsonSummaryTgrepIndex? CreateTgrepIndex(ToolReport report)
    {
        if (report.TgrepIndexTime is null)
            return null;

        return new JsonSummaryTgrepIndex
        {
            BuildTimeMilliseconds = report.TgrepIndexTime.Value.TotalMilliseconds
        };
    }
}

internal sealed class JsonSummaryLsCache
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("used")]
    public bool Used { get; init; }
}

internal sealed class JsonSummaryRoslynTarget
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("loadTimeMilliseconds")]
    public double? LoadTimeMilliseconds { get; init; }

    [JsonPropertyName("attemptedProjectCount")]
    public int? AttemptedProjectCount { get; init; }

    [JsonPropertyName("loadedProjectCount")]
    public int? LoadedProjectCount { get; init; }

    [JsonPropertyName("skippedProjectCount")]
    public int? SkippedProjectCount { get; init; }

    [JsonPropertyName("isPartial")]
    public bool IsPartial { get; init; }
}

internal sealed class JsonSummaryTgrepIndex
{
    [JsonPropertyName("buildTimeMilliseconds")]
    public double? BuildTimeMilliseconds { get; init; }
}

internal sealed class JsonSummaryQuery
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = [];

    [JsonPropertyName("algorithms")]
    public List<JsonSummaryAlgorithm> Algorithms { get; init; } = [];
}

internal sealed class JsonSummaryAlgorithm
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("elapsedMilliseconds")]
    public double? ElapsedMilliseconds { get; init; }

    [JsonPropertyName("characterCount")]
    public int CharacterCount { get; init; }

    [JsonPropertyName("lineCount")]
    public int LineCount { get; init; }
}
