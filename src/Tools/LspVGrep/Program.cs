using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LspVGrepTool.Algorithms;
using LspVGrepTool.Execution;
using LspVGrepTool.Infrastructure;
using LspVGrepTool.Models;
using LspVGrepTool.Reporting;

namespace LspVGrepTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: LspVGrepTool <input.json>");
            return 1;
        }

        var inputPath = Path.GetFullPath(args[0]);
        Log($"Reading input from '{inputPath}'.");
        var input = await InputLoader.LoadAsync(inputPath, CancellationToken.None);
        var queries = QueryRequestFactory.Create(input);
        var resolvedDirectory = ResolveTargetDirectory(inputPath, input.Directory!);
        Log($"Loaded {queries.Count} queries for '{resolvedDirectory}'.");

        using var context = new QueryExecutionContext(
            resolvedDirectory,
            new RoslynWorkspaceProvider(Log),
            new ExternalSearchRunner());

        // Eagerly load workspace so timing is separate from individual algorithm runs.
        Log("Loading Roslyn workspace.");
        var workspaceStopwatch = Stopwatch.StartNew();
        var workspace = await context.GetWorkspaceAsync(CancellationToken.None);
        workspaceStopwatch.Stop();
        Log($"Loaded {workspace.Solution.ProjectIds.Count} projects from {workspace.TargetKind} '{workspace.TargetPath}' in {FormatDuration(workspaceStopwatch.Elapsed)}.");

        var algorithms = new IQueryAlgorithm[]
        {
            new FindTypeDefinitionPwshAlgorithm(),
            new FindTypeDefinitionPwshSimpleAlgorithm(),
            new FindTypeDefinitionRoslynAlgorithm(),
            new FindTypeDefinitionRoslynLspAlgorithm(),
            new FindTypeDefinitionRoslynWorkspaceSymbolAlgorithm(),
            new FindInterfaceImplementationPwshAlgorithm(),
            new FindInterfaceImplementationRoslynAlgorithm(),
            new FindDerivedTypesPwshAlgorithm(),
            new FindDerivedTypesRoslynAlgorithm(),
            new FindMemberDefinitionPwshAlgorithm(),
            new FindMemberDefinitionRoslynAlgorithm()
        };

        Log($"Running {queries.Count} queries with {algorithms.Length} algorithms registered.");
        var executor = new QueryExecutor(algorithms, Log);
        var report = await executor.ExecuteAsync(queries, context, workspaceStopwatch.Elapsed, CancellationToken.None);

        var outputPath = ResolveOutputPath(inputPath, input.Output);
        Log($"Rendering report to '{outputPath}'.");
        var html = HtmlReportRenderer.Render(report);
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CancellationToken.None);
        Log("Report written.");

        Console.WriteLine(outputPath);
        return 0;
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
    }

    internal static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{elapsed.TotalMilliseconds:F0}ms";

    private static string ResolveTargetDirectory(string inputPath, string configuredDirectory)
    {
        var inputDirectory = Path.GetDirectoryName(inputPath)
            ?? throw new InvalidOperationException($"Could not resolve a parent directory for '{inputPath}'.");

        var candidatePath = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(inputDirectory, configuredDirectory);

        return Path.GetFullPath(candidatePath);
    }

    private static string ResolveOutputPath(string inputPath, string? configuredOutput)
    {
        var inputDirectory = Path.GetDirectoryName(inputPath)!;
        var fileName = configuredOutput ?? "result.html";
        return Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(inputDirectory, fileName);
    }
}
