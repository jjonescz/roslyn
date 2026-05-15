using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace LspVGrepTool.Infrastructure;

internal sealed record WorkspaceLoadResult(
    Workspace Workspace,
    Solution Solution,
    string TargetPath,
    RoslynLoadTargetKind TargetKind,
    int? AttemptedProjectCount,
    int LoadedProjectCount,
    int SkippedProjectCount);

internal sealed class RoslynWorkspaceProvider : IDisposable
{
    private readonly Action<string> _log;
    private readonly Action<string> _logProgress;
    private Workspace? _workspace;

    public RoslynWorkspaceProvider(Action<string>? log = null, Action<string>? logProgress = null)
    {
        _log = log ?? (_ => { });
        _logProgress = logProgress ?? (_ => { });
    }

    public async Task<WorkspaceLoadResult> LoadAsync(string directoryPath, bool useLscache, CancellationToken cancellationToken)
    {
        _log($"Discovering solution or projects under '{directoryPath}'.");
        var target = SolutionDiscovery.Find(directoryPath);
        _log($"Discovered {target.Kind}: {target.DisplayPath}.");

        if (useLscache)
        {
            var lscacheLoadResult = LsCacheWorkspaceLoader.TryLoad(directoryPath, target, _log, cancellationToken);
            if (lscacheLoadResult is not null)
            {
                _workspace = lscacheLoadResult.Workspace;
                return await WarmCompilationsAsync(lscacheLoadResult, cancellationToken);
            }
        }
        else
        {
            _log("Skipping LS cache hydration because it is disabled by input.");
        }

        MsBuildRegistration.EnsureRegistered();
        var workspace = MSBuildWorkspace.Create();
        _workspace = workspace;
        var progress = new Progress<ProjectLoadProgress>(ReportProjectLoadProgress);

        Solution solution;
        int? attemptedProjectCount = null;
        var skippedProjectCount = 0;
        switch (target.Kind)
        {
            case RoslynLoadTargetKind.Solution:
                _log($"Opening solution '{target.Paths[0]}'.");
                solution = await workspace.OpenSolutionAsync(target.Paths[0], progress: progress, cancellationToken: cancellationToken);
                attemptedProjectCount = solution.ProjectIds.Count;
                break;

            case RoslynLoadTargetKind.Project:
                _log($"Opening project '{target.Paths[0]}'.");
                solution = (await workspace.OpenProjectAsync(target.Paths[0], progress: progress, cancellationToken: cancellationToken)).Solution;
                attemptedProjectCount = 1;
                break;

            case RoslynLoadTargetKind.MultipleProjects:
                _log($"Opening {target.Paths.Count} projects.");
                attemptedProjectCount = target.Paths.Count;
                foreach (var projectPath in target.Paths)
                {
                    try
                    {
                        await workspace.OpenProjectAsync(projectPath, progress: progress, cancellationToken: cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        // Some projects may conflict (duplicate assembly names, etc.) — skip them.
                        skippedProjectCount++;
                        _log($"Skipping project '{projectPath}' because it could not be opened: {exception.GetType().Name}: {exception.Message}");
                    }
                }

                solution = workspace.CurrentSolution;
                break;

            default:
                throw new InvalidOperationException($"Unsupported Roslyn load target kind '{target.Kind}'.");
        }

        return await WarmCompilationsAsync(
            new WorkspaceLoadResult(workspace, solution, target.DisplayPath, target.Kind, attemptedProjectCount, solution.ProjectIds.Count, skippedProjectCount),
            cancellationToken);
    }

    private async Task<WorkspaceLoadResult> WarmCompilationsAsync(
        WorkspaceLoadResult result,
        CancellationToken cancellationToken)
    {
        // Strip unresolved analyzer references so solution-wide operations
        // (FindImplementationsAsync, FindDerivedClassesAsync) don't crash during checksumming.
        _log("Removing unresolved analyzer references.");
        var solution = RemoveUnresolvedAnalyzerReferences(result.Solution);

        // Warm up compilations so individual algorithm timings don't include lazy compilation cost.
        _log($"Warming compilations for {solution.ProjectIds.Count} projects.");
        await Task.WhenAll(solution.Projects.Select(p => p.GetCompilationAsync(cancellationToken)));
        _log("Compilation warmup complete.");

        return result with { Solution = solution };
    }

    private void ReportProjectLoadProgress(ProjectLoadProgress progress)
    {
        var targetFramework = string.IsNullOrWhiteSpace(progress.TargetFramework)
            ? string.Empty
            : $" ({progress.TargetFramework})";

        _logProgress($"Loading project: {progress.Operation} {Path.GetFileName(progress.FilePath)}{targetFramework} in {Program.FormatDuration(progress.ElapsedTime)}.");
    }

    private static Solution RemoveUnresolvedAnalyzerReferences(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var resolved = project.AnalyzerReferences
                .Where(r => r is not UnresolvedAnalyzerReference)
                .ToList();

            if (resolved.Count != project.AnalyzerReferences.Count)
            {
                solution = solution.WithProjectAnalyzerReferences(project.Id, resolved);
            }
        }

        return solution;
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}
