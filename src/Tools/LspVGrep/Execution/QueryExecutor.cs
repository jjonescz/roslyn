using System.Diagnostics;
using LspVGrepTool.Infrastructure;
using LspVGrepTool.Models;

namespace LspVGrepTool.Execution;

internal sealed class QueryExecutor
{
    private const int RoslynAlgorithmRunCount = 2;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<IQueryAlgorithm>> _algorithmsByQueryType;
    private readonly Action<string> _log;
    private readonly Action<string> _logProgress;

    public QueryExecutor(IEnumerable<IQueryAlgorithm> algorithms, Action<string>? log = null, Action<string>? logProgress = null)
    {
        _log = log ?? (_ => { });
        _logProgress = logProgress ?? (_ => { });
        _algorithmsByQueryType = algorithms
            .GroupBy(algorithm => algorithm.QueryType, StringComparer.Ordinal)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => (IReadOnlyList<IQueryAlgorithm>)grouping.ToList(),
                StringComparer.Ordinal);
    }

    public async Task<ToolReport> ExecuteAsync(
        IReadOnlyList<QueryRequest> queries,
        QueryExecutionContext context,
        string? repositoryCommitHash,
        long? sourceLineCount,
        TimeSpan? workspaceLoadTime,
        TimeSpan? tgrepIndexTime,
        CancellationToken cancellationToken)
    {
        var executionStopwatch = Stopwatch.StartNew();
        var queryReports = new List<QueryExecutionReport>(queries.Count);

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];
            if (!_algorithmsByQueryType.TryGetValue(query.Type, out var algorithms) || algorithms.Count == 0)
            {
                throw new InvalidOperationException($"No algorithms are registered for query type '{query.Type}'.");
            }

            var queryDescription = $"Query {queryIndex + 1}/{queries.Count}: {query.Type} ({FormatFields(query)})";
            _logProgress($"{queryDescription}; starting.");
            var results = new List<AlgorithmExecutionResult>(algorithms.Sum(GetRunCount));
            foreach (var algorithm in algorithms)
            {
                var runCount = GetRunCount(algorithm);
                for (var runIndex = 0; runIndex < runCount; runIndex++)
                {
                    var displayName = GetDisplayName(algorithm, runIndex, runCount);
                    var result = await ExecuteAlgorithmAsync(algorithm, displayName, query, context, queryDescription, _log, _logProgress, cancellationToken);
                    results.Add(result);
                }
            }

            queryReports.Add(new QueryExecutionReport(query.Type, query.GetDisplayFields(), results));
        }

        executionStopwatch.Stop();
        _log($"Completed {queries.Count} queries in {Program.FormatDuration(executionStopwatch.Elapsed)}.");

        var workspace = context.TryGetLoadedWorkspace();
        return new ToolReport(
            context.DirectoryPath,
            repositoryCommitHash,
            sourceLineCount,
            context.UseLscache,
            workspace?.TargetKind == RoslynLoadTargetKind.LsCache,
            workspace?.TargetPath,
            workspace?.TargetKind.ToString(),
            workspaceLoadTime,
            tgrepIndexTime,
            queryReports);
    }

    private static async Task<AlgorithmExecutionResult> ExecuteAlgorithmAsync(
        IQueryAlgorithm algorithm,
        string displayName,
        QueryRequest query,
        QueryExecutionContext context,
        string queryDescription,
        Action<string> log,
        Action<string> logProgress,
        CancellationToken cancellationToken)
    {
        logProgress($"{queryDescription}; running {displayName}.");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await algorithm.ExecuteAsync(query, context, cancellationToken);
            stopwatch.Stop();
            logProgress($"{queryDescription}; finished {displayName}: {result.Outcome} in {Program.FormatDuration(stopwatch.Elapsed)}.");
            return result with { AlgorithmName = displayName, ElapsedTime = stopwatch.Elapsed };
        }
        catch (Exception exception)
        {
            log($"{queryDescription}; failed {displayName}: {exception.GetType().Name}: {exception.Message}");
            return new AlgorithmExecutionResult(
                displayName,
                AlgorithmOutcome.Failed,
                exception.ToString());
        }
    }

    private static int GetRunCount(IQueryAlgorithm algorithm) =>
        IsRoslynAlgorithm(algorithm) ? RoslynAlgorithmRunCount : 1;

    private static bool IsRoslynAlgorithm(IQueryAlgorithm algorithm) =>
        algorithm.Name.Contains("roslyn", StringComparison.OrdinalIgnoreCase);

    private static string GetDisplayName(IQueryAlgorithm algorithm, int runIndex, int runCount) =>
        runCount == 1
            ? algorithm.Name
            : $"{algorithm.Name} (pass {runIndex + 1})";

    private static string FormatFields(QueryRequest query) =>
        string.Join(", ", query.GetDisplayFields().Select(static field => $"{field.Key}: {field.Value}"));
}
