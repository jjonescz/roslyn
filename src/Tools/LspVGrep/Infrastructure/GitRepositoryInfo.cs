using System.ComponentModel;
using System.Diagnostics;

namespace LspVGrepTool.Infrastructure;

internal static class GitRepositoryInfo
{
    public static async Task<string?> GetCommitHashAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var commitHash = await RunGitAsync(directoryPath, ["rev-parse", "--verify", "HEAD"], cancellationToken);
        return string.IsNullOrWhiteSpace(commitHash) ? null : commitHash;
    }

    public static async Task<string?> GetRemoteUrlAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var remoteUrl = await RunGitAsync(directoryPath, ["config", "--get", "remote.origin.url"], cancellationToken);
        return NormalizeRemoteUrl(remoteUrl);
    }

    private static async Task<string?> RunGitAsync(string directoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            return null;

        var output = (await standardOutputTask).Trim();
        return output.Length == 0 ? null : output;
    }

    private static string? NormalizeRemoteUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;

        var normalized = remoteUrl.Trim();
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://github.com/" + normalized["git@github.com:".Length..];
        }

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^".git".Length];
        }

        return normalized;
    }
}
