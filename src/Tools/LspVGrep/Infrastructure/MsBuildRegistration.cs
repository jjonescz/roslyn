using System.Runtime.InteropServices;
using Microsoft.Build.Locator;

namespace LspVGrepTool.Infrastructure;

internal static class MsBuildRegistration
{
    private static readonly object SyncRoot = new();
    private static bool s_registered;

    public static void EnsureRegistered()
    {
        if (s_registered || MSBuildLocator.IsRegistered)
        {
            s_registered = true;
            return;
        }

        lock (SyncRoot)
        {
            if (s_registered || MSBuildLocator.IsRegistered)
            {
                s_registered = true;
                return;
            }

            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            if (instances.Length > 0)
            {
                MSBuildLocator.RegisterInstance(instances[0]);
            }
            else
            {
                MSBuildLocator.RegisterMSBuildPath(GetDotnetSdkPath());
            }

            s_registered = true;
        }
    }

    private static string GetDotnetSdkPath()
    {
        var dotnetRoot = GetDotnetRoot();
        var sdkDirectory = Path.Combine(dotnetRoot, "sdk");
        var sdkPath = Directory.EnumerateDirectories(sdkDirectory)
            .Where(path => File.Exists(Path.Combine(path, "MSBuild.dll")))
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return sdkPath ?? throw new InvalidOperationException($"Could not locate a .NET SDK MSBuild under '{sdkDirectory}'.");
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
}
