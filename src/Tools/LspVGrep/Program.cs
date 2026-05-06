using System.Diagnostics;
using System.CommandLine;
using System.Text;
using LspVGrepTool.Algorithms;
using LspVGrepTool.Execution;
using LspVGrepTool.Infrastructure;
using LspVGrepTool.Models;
using LspVGrepTool.Reporting;

namespace LspVGrepTool;

internal static class Program
{
    private static readonly object s_logGate = new();
    private static int s_progressLineLength;
    private static bool s_progressLineActive;

    public static async Task<int> Main(string[] args)
    {
        var command = CreateCommand();
        return await command.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
    }

    private static RootCommand CreateCommand()
    {
        var legacyInputArgument = new Argument<string?>("input")
        {
            Description = "Input JSON file to run and render.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var rootCommand = new RootCommand
        {
            Description = "Run LspVGrep experiments and combine JSON summary reports."
        };

        rootCommand.Add(legacyInputArgument);
        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            var inputPath = parseResult.GetValue(legacyInputArgument);
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                Console.Error.WriteLine("Usage: LspVGrepTool <input.json>");
                Console.Error.WriteLine("       LspVGrepTool run <input.json>");
                Console.Error.WriteLine("       LspVGrepTool combine <report.json> [<report.json> ...] --output <combined.html>");
                return Task.FromResult(1);
            }

            return RunReportAsync(inputPath, cancellationToken);
        });

        var runInputArgument = new Argument<string>("input")
        {
            Description = "Input JSON file to run and render."
        };

        var runCommand = new Command("run", "Run an input JSON file and render HTML plus JSON reports.");
        runCommand.Add(runInputArgument);
        runCommand.SetAction((parseResult, cancellationToken) =>
            RunReportAsync(parseResult.GetValue(runInputArgument)!, cancellationToken));
        rootCommand.Add(runCommand);

        var reportsArgument = new Argument<string[]>("reports")
        {
            Description = "JSON summary reports to combine.",
            Arity = ArgumentArity.OneOrMore
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = "Combined HTML report output path. Defaults to the common result-file directory when possible."
        };

        var combineCommand = new Command("combine", "Combine JSON summary reports into an interactive HTML report.");
        combineCommand.Add(reportsArgument);
        combineCommand.Add(outputOption);
        combineCommand.SetAction((parseResult, cancellationToken) =>
            CombineReportsAsync(
                parseResult.GetValue(reportsArgument) ?? [],
                parseResult.GetValue(outputOption),
                cancellationToken));
        rootCommand.Add(combineCommand);

        return rootCommand;
    }

    private static async Task<int> RunReportAsync(string inputPathValue, CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(inputPathValue);
        Log($"Reading input from '{inputPath}'.");
        var input = await InputLoader.LoadAsync(inputPath, cancellationToken);
        var queries = QueryRequestFactory.Create(input);
        var resolvedDirectory = ResolveTargetDirectory(inputPath, input.Directory!);
        var useLscache = input.UseLscache ?? true;
        Log($"Loaded {queries.Count} queries for '{resolvedDirectory}'.");
        Log($"LS cache workspace hydration is {(useLscache ? "enabled" : "disabled")}.");

        var repositoryCommitHash = await GitRepositoryInfo.GetCommitHashAsync(resolvedDirectory, cancellationToken);
        Log(repositoryCommitHash is null
            ? "No Git commit hash was found for the inspected directory."
            : $"Inspected repository commit: {repositoryCommitHash}.");

        Log("Counting source lines.");
        var sourceLineCount = await SourceLineCounter.CountAsync(resolvedDirectory, cancellationToken);
        Log($"Counted {sourceLineCount:N0} non-empty source lines ({sourceLineCount / 1000.0:F1} kLOC).");

        using var context = new QueryExecutionContext(
            resolvedDirectory,
            useLscache,
            new RoslynWorkspaceProvider(Log, LogProgress),
            new ExternalSearchRunner());

        // Eagerly load workspace so timing is separate from individual algorithm runs.
        Log("Loading Roslyn workspace.");
        var workspaceStopwatch = Stopwatch.StartNew();
        var workspace = await context.GetWorkspaceAsync(cancellationToken);
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
        var executor = new QueryExecutor(algorithms, Log, LogProgress);
        var report = await executor.ExecuteAsync(queries, context, repositoryCommitHash, sourceLineCount, workspaceStopwatch.Elapsed, cancellationToken);

        var outputPath = ResolveOutputPath(inputPath, input.Output);
        Log($"Rendering report to '{outputPath}'.");
        var html = HtmlReportRenderer.Render(report);
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        Log("HTML report written.");

        var jsonOutputPath = ResolveJsonOutputPath(outputPath);
        Log($"Rendering summary JSON to '{jsonOutputPath}'.");
        var json = JsonReportRenderer.Render(report);
        await File.WriteAllTextAsync(jsonOutputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        Log("Summary JSON written.");

        Console.WriteLine(outputPath);
        return 0;
    }

    private static async Task<int> CombineReportsAsync(string[] reportPaths, string? outputPathValue, CancellationToken cancellationToken)
    {
        if (reportPaths.Length == 0)
        {
            Console.Error.WriteLine("At least one JSON summary report is required.");
            return 1;
        }

        var expandedReportPaths = ExpandReportPathPatterns(reportPaths, out var unmatchedPatterns);
        if (unmatchedPatterns.Count > 0)
        {
            foreach (var unmatchedPattern in unmatchedPatterns)
            {
                Console.Error.WriteLine($"Report pattern did not match any files: '{unmatchedPattern}'.");
            }

            return 1;
        }

        var reports = new List<JsonSummaryReport>(expandedReportPaths.Count);
        foreach (var reportPath in expandedReportPaths)
        {
            Log($"Reading summary JSON from '{reportPath}'.");
            reports.Add(await JsonSummaryReport.LoadAsync(reportPath, cancellationToken));
        }

        var outputPath = ResolveCombinedOutputPath(expandedReportPaths, outputPathValue);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Log($"Rendering combined report to '{outputPath}'.");
        var html = CombinedReportRenderer.Render(reports);
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        Log("Combined report written.");

        Console.WriteLine(outputPath);
        return 0;
    }

    private static string ResolveCombinedOutputPath(IReadOnlyList<string> reportPaths, string? outputPathValue)
    {
        if (!string.IsNullOrWhiteSpace(outputPathValue))
            return Path.GetFullPath(outputPathValue);

        var commonDirectory = GetCommonDirectory(reportPaths);
        var outputDirectory = commonDirectory ?? Environment.CurrentDirectory;
        return Path.Combine(outputDirectory, "combined-report.html");
    }

    private static string? GetCommonDirectory(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return null;

        var commonParts = SplitPath(Path.GetDirectoryName(filePaths[0]) ?? Environment.CurrentDirectory);
        foreach (var filePath in filePaths.Skip(1))
        {
            var directoryParts = SplitPath(Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory);
            var matchingPartCount = 0;

            while (matchingPartCount < commonParts.Count &&
                   matchingPartCount < directoryParts.Count &&
                   string.Equals(commonParts[matchingPartCount], directoryParts[matchingPartCount], StringComparison.OrdinalIgnoreCase))
            {
                matchingPartCount++;
            }

            commonParts = commonParts.Take(matchingPartCount).ToList();
            if (commonParts.Count == 0)
                return null;
        }

        var commonDirectory = CombinePathParts(commonParts);
        return IsRootDirectory(commonDirectory) ? null : commonDirectory;
    }

    private static List<string> SplitPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(root))
        {
            parts.Add(root);
        }

        var relativePath = string.IsNullOrEmpty(root)
            ? fullPath
            : fullPath[root.Length..];

        parts.AddRange(relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
        return parts;
    }

    private static string CombinePathParts(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
            return string.Empty;

        var path = parts[0];
        for (var index = 1; index < parts.Count; index++)
        {
            path = Path.Combine(path, parts[index]);
        }

        return path;
    }

    private static bool IsRootDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExpandReportPathPatterns(string[] reportPathValues, out List<string> unmatchedPatterns)
    {
        var expandedReportPaths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        unmatchedPatterns = [];

        foreach (var reportPathValue in reportPathValues)
        {
            var matchedPaths = ExpandReportPathPattern(reportPathValue).ToList();
            if (matchedPaths.Count == 0)
            {
                unmatchedPatterns.Add(reportPathValue);
                continue;
            }

            foreach (var matchedPath in matchedPaths)
            {
                if (seenPaths.Add(matchedPath))
                {
                    expandedReportPaths.Add(matchedPath);
                }
            }
        }

        return expandedReportPaths;
    }

    private static IEnumerable<string> ExpandReportPathPattern(string reportPathValue)
    {
        if (!ContainsGlob(reportPathValue))
        {
            yield return Path.GetFullPath(reportPathValue);
            yield break;
        }

        var directoryPath = Path.GetDirectoryName(reportPathValue);
        var filePattern = Path.GetFileName(reportPathValue);
        var searchRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(directoryPath) ? "." : directoryPath);

        if (string.IsNullOrWhiteSpace(filePattern) || !Directory.Exists(searchRoot))
        {
            yield break;
        }

        foreach (var filePath in Directory.EnumerateFiles(searchRoot, filePattern).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetFullPath(filePath);
        }
    }

    private static bool ContainsGlob(string path) =>
        path.Contains('*', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal);

    private static void Log(string message)
    {
        lock (s_logGate)
        {
            ClearProgressLineIfNeeded();
            Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        }
    }

    private static void LogProgress(string message)
    {
        if (Console.IsErrorRedirected)
            return;

        lock (s_logGate)
        {
            var text = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
            var padding = s_progressLineLength > text.Length
                ? new string(' ', s_progressLineLength - text.Length)
                : string.Empty;

            Console.Error.Write($"\r{text}{padding}");
            s_progressLineLength = Math.Max(s_progressLineLength, text.Length);
            s_progressLineActive = true;
        }
    }

    private static void ClearProgressLineIfNeeded()
    {
        if (!s_progressLineActive)
            return;

        Console.Error.Write($"\r{new string(' ', s_progressLineLength)}\r");
        s_progressLineLength = 0;
        s_progressLineActive = false;
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

    private static string ResolveJsonOutputPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory ?? string.Empty, fileNameWithoutExtension + ".json");
    }
}
