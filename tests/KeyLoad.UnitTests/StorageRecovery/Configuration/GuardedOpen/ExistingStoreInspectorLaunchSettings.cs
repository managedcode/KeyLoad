using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectorLaunchSettings
{
    private const string DotnetCommand = "dotnet";
    private const string SolutionName = "KeyLoad.slnx";
    private const string RecoveryHostProject = "KeyLoad.RecoveryHost.csproj";
    private const string TestsDirectory = "tests";
    private const string RecoveryHostDirectory = "KeyLoad.RecoveryHost";
    private const string BuildOutputDirectory = "bin";
    private const string ReleaseConfiguration = "Release";
    private const string TargetFramework = "net10.0";
    private const string RecoveryHostAssembly = "KeyLoad.RecoveryHost.dll";
    private const string InspectorMode = "existing-store-inspect";
    private const string RepositoryMissingMessage = "The KeyLoad repository root is unavailable.";
    private const string ReleaseHostMissingMessage = "The Release RecoveryHost assembly is not built.";

    internal static (ProcessStartInfo StartInfo, string AssemblyPath) Create()
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, TestsDirectory, RecoveryHostDirectory, BuildOutputDirectory,
            ReleaseConfiguration, TargetFramework, RecoveryHostAssembly);
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(ReleaseHostMissingMessage, assembly);
        }

        var start = new ProcessStartInfo(DotnetCommand)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(InspectorMode);
        return (start, assembly);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var origin in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionName))
                    && File.Exists(Path.Combine(directory.FullName, TestsDirectory, RecoveryHostDirectory, RecoveryHostProject)))
                {
                    return directory.FullName;
                }
            }
        }
        throw new DirectoryNotFoundException(RepositoryMissingMessage);
    }
}
