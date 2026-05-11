using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LspVGrepTool.Infrastructure;

internal sealed class ExternalSearchRunner
{
    private string? _tgrepIndexPath;

    public async Task<ExternalSearchResult> BuildTgrepIndexAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var indexPath = GetTgrepIndexPath(directoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

        var result = await TryRunAsync(
            fileName: "tgrep",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("index");
                arguments.Add("--index-path");
                arguments.Add(indexPath);
                arguments.Add("--exclude");
                arguments.Add("bin");
                arguments.Add("--exclude");
                arguments.Add("obj");
                arguments.Add(".");
            });

        if (result.ExitCode == 0)
        {
            _tgrepIndexPath = indexPath;
        }

        return result;
    }

    public async Task<ExternalSearchResult> SearchTypeDefinitionPwshAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b(class|record|struct|interface|enum)\s+(class\s+|struct\s+)?{Regex.Escape(typeName)}\b";

        // Build a one-liner that uses Get-ChildItem + Select-String, excluding bin/obj,
        // and formats output as  file:line: matchedLine  (grep-style).
        var script = string.Join(" ",
            $"Get-ChildItem -Path '{EscapePwshString(directoryPath)}' -Recurse -Filter '*.cs'",
            "| Where-Object { $_.FullName -notmatch '\\\\(bin|obj)\\\\' }",
            $"| Select-String -Pattern '{EscapePwshString(pattern)}'",
            "| ForEach-Object { \"$($_.Path):$($_.LineNumber): $($_.Line.TrimStart())\" }");

        return await TryRunAsync(
            fileName: "pwsh",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(script);
            });
    }

    public async Task<ExternalSearchResult> SearchTypeDefinitionTgrepAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b(class|record|struct|interface|enum)\s+(class\s+|struct\s+)?{Regex.Escape(typeName)}\b";
        return await SearchTgrepAsync(_tgrepIndexPath, directoryPath, pattern, cancellationToken);
    }

    public async Task<ExternalSearchResult> SearchTypeNamePwshAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b{Regex.Escape(typeName)}\b";

        var script = string.Join(" ",
            $"Get-ChildItem -Path '{EscapePwshString(directoryPath)}' -Recurse -Filter '*.cs'",
            "| Where-Object { $_.FullName -notmatch '\\\\(bin|obj)\\\\' }",
            $"| Select-String -Pattern '{EscapePwshString(pattern)}'",
            "| ForEach-Object { \"$($_.Path):$($_.LineNumber): $($_.Line.TrimStart())\" }");

        return await TryRunAsync(
            fileName: "pwsh",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(script);
            });
    }

    public async Task<ExternalSearchResult> SearchTypeNameTgrepAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b{Regex.Escape(typeName)}\b";
        return await SearchTgrepAsync(_tgrepIndexPath, directoryPath, pattern, cancellationToken);
    }

    public async Task<ExternalSearchResult> SearchMemberDefinitionPwshAsync(
        string directoryPath,
        string memberName,
        CancellationToken cancellationToken)
    {
        // Match lines where a visibility/type keyword precedes the member name,
        // followed by ( (method), < (generic), { (property), = or ; (field).
        var pattern = $@"\b(void|bool|int|long|float|double|string|char|byte|decimal|object|var|Task|static|async|public|private|protected|internal|override|virtual|abstract|sealed|readonly|new|partial|extern)\s+.*\b{Regex.Escape(memberName)}\b\s*[\(<\{{=;]";

        var script = string.Join(" ",
            $"Get-ChildItem -Path '{EscapePwshString(directoryPath)}' -Recurse -Filter '*.cs'",
            "| Where-Object { $_.FullName -notmatch '\\\\(bin|obj)\\\\' }",
            $"| Select-String -Pattern '{EscapePwshString(pattern)}'",
            "| ForEach-Object { \"$($_.Path):$($_.LineNumber): $($_.Line.TrimStart())\" }");

        return await TryRunAsync(
            fileName: "pwsh",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(script);
            });
    }

    public async Task<ExternalSearchResult> SearchMemberDefinitionTgrepAsync(
        string directoryPath,
        string memberName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b(void|bool|int|long|float|double|string|char|byte|decimal|object|var|Task|static|async|public|private|protected|internal|override|virtual|abstract|sealed|readonly|new|partial|extern)\s+.*\b{Regex.Escape(memberName)}\b\s*[\(<\{{=;]";
        return await SearchTgrepAsync(_tgrepIndexPath, directoryPath, pattern, cancellationToken);
    }

    private static string EscapePwshString(string value) =>
        value.Replace("'", "''");

    public async Task<ExternalSearchResult> SearchImplementationPwshAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@":\s.*\b{Regex.Escape(typeName)}\b";

        var script = string.Join(" ",
            $"Get-ChildItem -Path '{EscapePwshString(directoryPath)}' -Recurse -Filter '*.cs'",
            "| Where-Object { $_.FullName -notmatch '\\\\(bin|obj)\\\\' }",
            $"| Select-String -Pattern '{EscapePwshString(pattern)}'",
            "| ForEach-Object { \"$($_.Path):$($_.LineNumber): $($_.Line.TrimStart())\" }");

        return await TryRunAsync(
            fileName: "pwsh",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(script);
            });
    }

    public async Task<ExternalSearchResult> SearchImplementationTgrepAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@":\s.*\b{Regex.Escape(typeName)}\b";
        return await SearchTgrepAsync(_tgrepIndexPath, directoryPath, pattern, cancellationToken);
    }

    public async Task<ExternalSearchResult> SearchDerivedTypesPwshAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b(class|record|struct)\s+\w+.*\b{Regex.Escape(typeName)}\b";

        var script = string.Join(" ",
            $"Get-ChildItem -Path '{EscapePwshString(directoryPath)}' -Recurse -Filter '*.cs'",
            "| Where-Object { $_.FullName -notmatch '\\\\(bin|obj)\\\\' }",
            $"| Select-String -Pattern '{EscapePwshString(pattern)}'",
            "| ForEach-Object { \"$($_.Path):$($_.LineNumber): $($_.Line.TrimStart())\" }");

        return await TryRunAsync(
            fileName: "pwsh",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("-NoProfile");
                arguments.Add("-NonInteractive");
                arguments.Add("-Command");
                arguments.Add(script);
            });
    }

    public async Task<ExternalSearchResult> SearchDerivedTypesTgrepAsync(
        string directoryPath,
        string typeName,
        CancellationToken cancellationToken)
    {
        var pattern = $@"\b(class|record|struct)\s+\w+.*\b{Regex.Escape(typeName)}\b";
        return await SearchTgrepAsync(_tgrepIndexPath, directoryPath, pattern, cancellationToken);
    }

    private static async Task<ExternalSearchResult> SearchTgrepAsync(
        string? indexPath,
        string directoryPath,
        string pattern,
        CancellationToken cancellationToken)
    {
        return await TryRunAsync(
            fileName: "tgrep",
            directoryPath,
            cancellationToken,
            argumentBuilder: arguments =>
            {
                arguments.Add("--no-heading");
                arguments.Add("--color");
                arguments.Add("never");
                arguments.Add("--trim");
                if (indexPath is not null)
                {
                    arguments.Add("--index-path");
                    arguments.Add(indexPath);
                }

                arguments.Add("-t");
                arguments.Add("cs");
                arguments.Add("--glob");
                arguments.Add("!**/bin/**");
                arguments.Add("--glob");
                arguments.Add("!**/obj/**");
                arguments.Add(pattern);
                arguments.Add(".");
            });
    }

    private static string GetTgrepIndexPath(string directoryPath)
    {
        var fullPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath))).ToLowerInvariant();
        var directoryName = Path.GetFileName(fullPath);
        var indexDirectoryName = string.IsNullOrWhiteSpace(directoryName)
            ? hash
            : $"{SanitizeFileName(directoryName)}-{hash[..12]}";

        return Path.Combine(Path.GetTempPath(), "LspVGrepTool", "tgrep-indexes", indexDirectoryName);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static async Task<ExternalSearchResult> TryRunAsync(
        string fileName,
        string directoryPath,
        CancellationToken cancellationToken,
        Action<System.Collections.ObjectModel.Collection<string>> argumentBuilder)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = directoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        argumentBuilder(startInfo.ArgumentList);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ExternalSearchResult.CommandNotFound(fileName);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new ExternalSearchResult(fileName, process.ExitCode, standardOutput, standardError, CommandMissing: false);
    }
}

internal sealed record ExternalSearchResult(
    string ToolName,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool CommandMissing)
{
    public static ExternalSearchResult CommandNotFound(string toolName) =>
        new(toolName, -1, string.Empty, string.Empty, CommandMissing: true);
}
