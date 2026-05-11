using LspVGrepTool.Execution;
using LspVGrepTool.Infrastructure;
using LspVGrepTool.Models;

namespace LspVGrepTool.Algorithms;

internal sealed class FindInterfaceImplementationTgrepAlgorithm : QueryAlgorithm<FindInterfaceImplementationQuery>
{
    public override string Name => "find-interface-implementation-tgrep";

    public override string QueryType => QueryTypes.FindInterfaceImplementation;

    protected override async Task<AlgorithmExecutionResult> ExecuteTypedAsync(
        FindInterfaceImplementationQuery query,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var summary = $"tgrep search for ': ...{query.Name}'";
        var searchResult = await context.SearchImplementationTgrepAsync(query.Name, cancellationToken);
        return CreateResult(searchResult, summary);
    }

    private AlgorithmExecutionResult CreateResult(ExternalSearchResult searchResult, string summary)
    {
        if (searchResult.CommandMissing)
            return new AlgorithmExecutionResult(Name, AlgorithmOutcome.Failed, "'tgrep' was not available on PATH.", summary);

        if (searchResult.ExitCode != 0 && !string.IsNullOrWhiteSpace(searchResult.StandardError))
            return new AlgorithmExecutionResult(Name, AlgorithmOutcome.Failed, searchResult.StandardError.Trim(), summary);

        var responseText = string.IsNullOrWhiteSpace(searchResult.StandardOutput)
            ? ""
            : searchResult.StandardOutput.TrimEnd();

        return new AlgorithmExecutionResult(Name, AlgorithmOutcome.Succeeded, responseText, summary);
    }
}
