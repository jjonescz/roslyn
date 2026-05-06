using System.ComponentModel;
using System.Diagnostics;

namespace LspVGrepTool.Infrastructure;

internal static class GitRepositoryInfo
{
    public static async Task<string?> GetCommitHashAsync(string directoryPath, CancellationToken cancellationToken)
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

        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--verify");
        startInfo.ArgumentList.Add("HEAD");

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

        var commitHash = (await standardOutputTask).Trim();
        return commitHash.Length == 0 ? null : commitHash;
    }
}
