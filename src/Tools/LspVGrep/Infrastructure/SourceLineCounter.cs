namespace LspVGrepTool.Infrastructure;

internal static class SourceLineCounter
{
    private static readonly HashSet<string> s_sourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".vb",
        ".fs",
        ".csx",
        ".c",
        ".cc",
        ".cpp",
        ".cxx",
        ".h",
        ".hpp",
        ".hxx",
        ".ts",
        ".tsx",
        ".js",
        ".jsx"
    };

    private static readonly HashSet<string> s_ignoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "artifacts",
        "bin",
        "node_modules",
        "obj",
        "out",
        "packages",
        "TestResults"
    };

    public static async Task<long> CountAsync(string directoryPath, CancellationToken cancellationToken)
    {
        long lineCount = 0;
        foreach (var filePath in EnumerateSourceFiles(directoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineCount += await CountNonEmptyLinesAsync(filePath, cancellationToken);
        }

        return lineCount;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directoryPath)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(directoryPath);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (!s_ignoredDirectoryNames.Contains(Path.GetFileName(childDirectory)))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (s_sourceExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static async Task<long> CountNonEmptyLinesAsync(string filePath, CancellationToken cancellationToken)
    {
        long lineCount = 0;
        try
        {
            using var reader = new StreamReader(filePath);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lineCount++;
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
        }

        return lineCount;
    }
}
