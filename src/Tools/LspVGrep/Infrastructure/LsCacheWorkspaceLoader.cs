using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace LspVGrepTool.Infrastructure;

internal static class LsCacheWorkspaceLoader
{
    public static WorkspaceLoadResult? TryLoad(
        string directoryPath,
        RoslynLoadTarget target,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var cachePaths = GetCachePaths(directoryPath, target).ToArray();
        if (cachePaths.Length == 0)
        {
            log("No LS cache files were found; falling back to MSBuild workspace load.");
            return null;
        }

        log($"Hydrating workspace from {cachePaths.Length} LS cache files.");
        var workspace = new AdhocWorkspace();
        var loadedSlices = 0;
        var skippedSlices = 0;

        foreach (var cachePath in cachePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var slice in LsCacheFile.Parse(cachePath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.Equals(slice.Language, "C#", StringComparison.OrdinalIgnoreCase))
                {
                    skippedSlices++;
                    continue;
                }

                try
                {
                    var projectInfo = CommandLineProject.CreateProjectInfo(
                        slice.GetProjectName(),
                        LanguageNames.CSharp,
                        slice.GetCommandLineArguments(),
                        slice.ProjectDirectory,
                        workspace);

                    workspace.AddProject(projectInfo);
                    loadedSlices++;
                }
                catch (Exception exception)
                {
                    skippedSlices++;
                    log($"Skipping LS cache slice '{slice.DisplayName}': {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        if (loadedSlices == 0)
        {
            workspace.Dispose();
            log($"No C# projects could be loaded from {cachePaths.Length} LS cache files; falling back to MSBuild workspace load.");
            return null;
        }

        var targetPath = $"{loadedSlices} LS cache project{(loadedSlices == 1 ? string.Empty : "s")} from {cachePaths.Length} cache file{(cachePaths.Length == 1 ? string.Empty : "s")}";
        if (skippedSlices > 0)
        {
            targetPath += $" ({skippedSlices} skipped)";
        }

        log($"Hydrated {targetPath}.");
        return new WorkspaceLoadResult(workspace, workspace.CurrentSolution, targetPath, RoslynLoadTargetKind.LsCache);
    }

    private static IEnumerable<string> GetCachePaths(string directoryPath, RoslynLoadTarget target)
    {
        return target.Kind switch
        {
            RoslynLoadTargetKind.Project => GetProjectCachePaths(target.Paths),
            RoslynLoadTargetKind.MultipleProjects => GetProjectCachePaths(target.Paths),
            _ => Directory.EnumerateFiles(directoryPath, "*.csproj.lscache", SearchOption.AllDirectories)
                .Where(path => !IsBuildArtifact(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<string> GetProjectCachePaths(IEnumerable<string> projectPaths)
    {
        foreach (var projectPath in projectPaths)
        {
            var cachePath = projectPath + ".lscache";
            if (File.Exists(cachePath))
            {
                yield return cachePath;
            }
        }
    }

    private static bool IsBuildArtifact(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LsCacheFile
    {
        private readonly string _cachePath;
        private readonly string _cacheDirectory;
        private readonly Dictionary<string, string> _pathPrefixes = [];

        private string? _section;
        private string? _language;
        private Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _sliceDimensions = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _commandLineArguments = [];
        private List<string> _sourceFiles = [];
        private List<string> _metadataReferences = [];
        private List<string> _analyzerReferences = [];
        private List<string> _analyzerConfigFiles = [];
        private List<string> _additionalFiles = [];

        private LsCacheFile(string cachePath)
        {
            _cachePath = cachePath;
            _cacheDirectory = Path.GetDirectoryName(cachePath)
                ?? throw new InvalidOperationException($"Could not resolve a parent directory for '{cachePath}'.");
        }

        public static IEnumerable<LsCacheSlice> Parse(string cachePath)
        {
            var parser = new LsCacheFile(cachePath);
            foreach (var line in File.ReadLines(cachePath))
            {
                if (line.Trim() == "---")
                {
                    var slice = parser.TryCreateSlice();
                    if (slice is not null)
                    {
                        yield return slice;
                    }

                    parser.ResetSlice();
                    continue;
                }

                parser.ParseLine(line);
            }

            var finalSlice = parser.TryCreateSlice();
            if (finalSlice is not null)
            {
                yield return finalSlice;
            }
        }

        private void ParseLine(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                return;

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                _section = trimmed[1..^1];
                _pathPrefixes.Clear();
                return;
            }

            switch (_section)
            {
                case "project":
                    if (TrySplitKeyValue(trimmed, out var projectKey, out var projectValue) &&
                        projectKey.Equals("language", StringComparison.OrdinalIgnoreCase))
                    {
                        _language = projectValue;
                    }

                    break;

                case "sliceDimensions":
                    AddKeyValue(_sliceDimensions, trimmed);
                    break;

                case "properties":
                    AddKeyValue(_properties, trimmed);
                    break;

                case "commandLineArguments":
                    _commandLineArguments.Add(ResolvePlaceholders(trimmed));
                    break;

                case "sourceFiles":
                    AddHierarchicalPath(line, _sourceFiles);
                    break;

                case "metadataReferences":
                    AddHierarchicalPath(line, _metadataReferences);
                    break;

                case "analyzerReferences":
                    AddHierarchicalPath(line, _analyzerReferences);
                    break;

                case "analyzerConfigFiles":
                    AddHierarchicalPath(line, _analyzerConfigFiles);
                    break;

                case "additionalFiles":
                    AddHierarchicalPath(line, _additionalFiles);
                    break;
            }
        }

        private void AddHierarchicalPath(string line, List<string> paths)
        {
            var indent = line.Length - line.TrimStart().Length;
            var value = line.Trim();
            if (value.Length == 0)
                return;

            if (value.EndsWith('/') || value.EndsWith('\\'))
            {
                _pathPrefixes[indent.ToString()] = ResolvePlaceholders(value);

                foreach (var key in _pathPrefixes.Keys.Where(key => int.Parse(key) > indent).ToArray())
                {
                    _pathPrefixes.Remove(key);
                }

                return;
            }

            var prefix = string.Concat(_pathPrefixes
                .Where(pair => int.Parse(pair.Key) < indent)
                .OrderBy(pair => int.Parse(pair.Key))
                .Select(pair => pair.Value));

            paths.Add(ResolvePath(prefix + value));
        }

        private LsCacheSlice? TryCreateSlice()
        {
            if (_language is null || _commandLineArguments.Count == 0 || _sourceFiles.Count == 0)
                return null;

            var projectPath = _cachePath.EndsWith(".lscache", StringComparison.OrdinalIgnoreCase)
                ? _cachePath[..^".lscache".Length]
                : _cachePath;

            var projectDirectory = Path.GetDirectoryName(projectPath) ?? _cacheDirectory;
            return new LsCacheSlice(
                _cachePath,
                projectPath,
                projectDirectory,
                _language,
                new Dictionary<string, string>(_properties, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(_sliceDimensions, StringComparer.OrdinalIgnoreCase),
                [.. _commandLineArguments],
                [.. _sourceFiles.Where(File.Exists)],
                [.. _metadataReferences.Where(File.Exists)],
                [.. _analyzerReferences.Where(File.Exists)],
                [.. _analyzerConfigFiles.Where(File.Exists)],
                [.. _additionalFiles.Where(File.Exists)]);
        }

        private void ResetSlice()
        {
            _section = null;
            _language = null;
            _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sliceDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _commandLineArguments = [];
            _sourceFiles = [];
            _metadataReferences = [];
            _analyzerReferences = [];
            _analyzerConfigFiles = [];
            _additionalFiles = [];
            _pathPrefixes.Clear();
        }

        private string ResolvePlaceholders(string value)
        {
            return value
                .Replace("<PATH>", _cacheDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                .Replace("<DOTNET>", GetDotnetRoot(), StringComparison.OrdinalIgnoreCase)
                .Replace("<NUGET>", GetNuGetPackageRoot(), StringComparison.OrdinalIgnoreCase);
        }

        private string ResolvePath(string value)
        {
            var resolved = ResolvePlaceholders(value).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(resolved)
                ? resolved
                : Path.Combine(_cacheDirectory, resolved));
        }

        private static void AddKeyValue(Dictionary<string, string> values, string line)
        {
            if (TrySplitKeyValue(line, out var key, out var value))
            {
                values[key] = value;
            }
        }

        private static bool TrySplitKeyValue(string line, out string key, out string value)
        {
            var equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
            {
                key = string.Empty;
                value = string.Empty;
                return false;
            }

            key = line[..equalsIndex];
            value = line[(equalsIndex + 1)..];
            return true;
        }

        private static string GetDotnetRoot()
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
                return dotnetRoot;

            var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            return runtimeDirectory.Parent?.Parent?.Parent?.FullName
                ?? throw new InvalidOperationException("Could not locate the .NET installation root.");
        }

        private static string GetNuGetPackageRoot()
        {
            var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (!string.IsNullOrWhiteSpace(nugetPackages))
                return nugetPackages;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".nuget", "packages");
        }
    }

    private sealed record LsCacheSlice(
        string CachePath,
        string ProjectPath,
        string ProjectDirectory,
        string Language,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyDictionary<string, string> SliceDimensions,
        IReadOnlyList<string> CommandLineArguments,
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<string> MetadataReferences,
        IReadOnlyList<string> AnalyzerReferences,
        IReadOnlyList<string> AnalyzerConfigFiles,
        IReadOnlyList<string> AdditionalFiles)
    {
        public string DisplayName => GetProjectName();

        public string GetProjectName()
        {
            var projectName = Properties.TryGetValue("AssemblyName", out var assemblyName) && !string.IsNullOrWhiteSpace(assemblyName)
                ? assemblyName
                : Path.GetFileNameWithoutExtension(ProjectPath);

            var targetFramework = SliceDimensions.TryGetValue("TargetFramework", out var tfm) && !string.IsNullOrWhiteSpace(tfm)
                ? tfm
                : Properties.TryGetValue("TemporaryDependencyNodeTargetIdentifier", out var fallbackTfm) ? fallbackTfm : null;

            return string.IsNullOrWhiteSpace(targetFramework)
                ? projectName
                : $"{projectName} ({targetFramework})";
        }

        public IEnumerable<string> GetCommandLineArguments()
        {
            foreach (var argument in CommandLineArguments)
            {
                yield return argument;
            }

            foreach (var metadataReference in MetadataReferences)
            {
                yield return $"/reference:{metadataReference}";
            }

            foreach (var analyzerReference in AnalyzerReferences)
            {
                yield return $"/analyzer:{analyzerReference}";
            }

            foreach (var analyzerConfigFile in AnalyzerConfigFiles)
            {
                yield return $"/analyzerconfig:{analyzerConfigFile}";
            }

            foreach (var additionalFile in AdditionalFiles)
            {
                yield return $"/additionalfile:{additionalFile}";
            }

            foreach (var sourceFile in SourceFiles)
            {
                yield return sourceFile;
            }
        }
    }
}
