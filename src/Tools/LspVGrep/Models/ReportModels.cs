namespace LspVGrepTool.Models;

internal sealed record ToolReport(
    string Directory,
    string? RepositoryCommitHash,
    long? SourceLineCount,
    bool LsCacheEnabled,
    bool LsCacheUsed,
    string? RoslynTargetPath,
    string? RoslynTargetKind,
    TimeSpan? RoslynLoadTime,
    int? RoslynAttemptedProjectCount,
    int? RoslynLoadedProjectCount,
    int? RoslynSkippedProjectCount,
    TimeSpan? TgrepIndexTime,
    IReadOnlyList<QueryExecutionReport> Queries);

internal sealed record QueryExecutionReport(
    string Type,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<AlgorithmExecutionResult> Algorithms);

internal enum AlgorithmOutcome
{
    Succeeded,
    Failed
}

internal sealed record AlgorithmExecutionResult(
    string AlgorithmName,
    AlgorithmOutcome Outcome,
    string ResponseText,
    string Summary = "",
    TimeSpan ElapsedTime = default)
{
    public int CharacterCount => ResponseText.Length;
    public int LineCount => string.IsNullOrEmpty(ResponseText) ? 0 : ResponseText.Count(static character => character == '\n') + 1;
}
