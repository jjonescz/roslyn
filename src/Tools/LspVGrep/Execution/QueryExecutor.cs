using System.Diagnostics;
using LspVGrepTool.Models;

namespace LspVGrepTool.Execution;

internal sealed class QueryExecutor
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IQueryAlgorithm>> _algorithmsByQueryType;
    private readonly Action<string> _log;

    public QueryExecutor(IEnumerable<IQueryAlgorithm> algorithms, Action<string>? log = null)
    {
        _log = log ?? (_ => { });
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
        TimeSpan? workspaceLoadTime,
        CancellationToken cancellationToken)
    {
        var queryReports = new List<QueryExecutionReport>(queries.Count);

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];
            if (!_algorithmsByQueryType.TryGetValue(query.Type, out var algorithms) || algorithms.Count == 0)
            {
                throw new InvalidOperationException($"No algorithms are registered for query type '{query.Type}'.");
            }

            _log($"Query {queryIndex + 1}/{queries.Count}: {query.Type} ({FormatFields(query)}).");
            var results = new List<AlgorithmExecutionResult>(algorithms.Count);
            foreach (var algorithm in algorithms)
            {
                var result = await ExecuteAlgorithmAsync(algorithm, query, context, _log, cancellationToken);
                results.Add(result);
            }

            queryReports.Add(new QueryExecutionReport(query.Type, query.GetDisplayFields(), results));
        }

        var workspace = context.TryGetLoadedWorkspace();
        return new ToolReport(
            context.DirectoryPath,
            workspace?.TargetPath,
            workspace?.TargetKind.ToString(),
            workspaceLoadTime,
            queryReports);
    }

    private static async Task<AlgorithmExecutionResult> ExecuteAlgorithmAsync(
        IQueryAlgorithm algorithm,
        QueryRequest query,
        QueryExecutionContext context,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"  Starting {algorithm.Name}.");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await algorithm.ExecuteAsync(query, context, cancellationToken);
            stopwatch.Stop();
            log($"  Finished {algorithm.Name}: {result.Outcome} in {Program.FormatDuration(stopwatch.Elapsed)}.");
            return result with { ElapsedTime = stopwatch.Elapsed };
        }
        catch (Exception exception)
        {
            log($"  Failed {algorithm.Name}: {exception.GetType().Name}: {exception.Message}");
            return new AlgorithmExecutionResult(
                algorithm.Name,
                AlgorithmOutcome.Failed,
                exception.ToString());
        }
    }

    private static string FormatFields(QueryRequest query) =>
        string.Join(", ", query.GetDisplayFields().Select(static field => $"{field.Key}: {field.Value}"));
}
