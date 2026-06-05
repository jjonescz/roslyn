using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using LspVGrepTool.Execution;
using LspVGrepTool.Models;

namespace LspVGrepTool.Algorithms;

internal sealed class FindInterfaceImplementationRoslynAlgorithm : QueryAlgorithm<FindInterfaceImplementationQuery>
{
    public override string Name => "roslyn-find-implementations";

    public override string QueryType => QueryTypes.FindInterfaceImplementation;

    protected override async Task<AlgorithmExecutionResult> ExecuteTypedAsync(
        FindInterfaceImplementationQuery query,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var workspace = await context.GetWorkspaceAsync(cancellationToken);

        var targetSymbols = new List<INamedTypeSymbol>();
        var targetSymbolKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in workspace.Solution.Projects)
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(
                project, query.Name, ignoreCase: false, SymbolFilter.Type, cancellationToken);
            foreach (var symbol in declarations.OfType<INamedTypeSymbol>())
            {
                if (targetSymbolKeys.Add(GetSymbolKey(symbol)))
                {
                    targetSymbols.Add(symbol);
                }
            }
        }

        if (targetSymbols.Count == 0)
        {
            var notFoundSummary = $"called SymbolFinder.FindDeclarationsAsync for '{query.Name}' — not found";
            return new AlgorithmExecutionResult(Name, AlgorithmOutcome.Succeeded,
                "",
                notFoundSummary);
        }

        var results = new List<INamedTypeSymbol>();
        foreach (var targetSymbol in targetSymbols)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(
                targetSymbol, workspace.Solution, cancellationToken: cancellationToken);
            results.AddRange(implementations.OfType<INamedTypeSymbol>());
        }

        return FormatResults(
            results,
            $"called SymbolFinder.FindImplementationsAsync for {targetSymbols.Count} '{query.Name}' declaration(s): {FormatTargetSymbols(targetSymbols)}");
    }

    private static string GetSymbolKey(INamedTypeSymbol symbol)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (sourceLocation is null)
            return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var span = sourceLocation.GetLineSpan();
        return $"{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} - {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
    }

    private static string FormatTargetSymbols(IReadOnlyList<INamedTypeSymbol> targetSymbols) =>
        string.Join(", ", targetSymbols.Select(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)).Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal));

    private AlgorithmExecutionResult FormatResults(IEnumerable<INamedTypeSymbol> symbols, string summary)
    {
        var matches = new List<string>();
        foreach (var symbol in symbols)
        {
            foreach (var location in symbol.Locations.Where(loc => loc.IsInSource))
            {
                var span = location.GetLineSpan();
                matches.Add($"{symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} - {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})");
            }
        }

        var distinct = matches.Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();
        var lines = distinct;

        return new AlgorithmExecutionResult(Name, AlgorithmOutcome.Succeeded, string.Join(Environment.NewLine, lines), summary);
    }
}
