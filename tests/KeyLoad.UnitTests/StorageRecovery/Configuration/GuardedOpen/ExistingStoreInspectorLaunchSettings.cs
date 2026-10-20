using System.Diagnostics;
using KeyLoad.RecoveryTests;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectorLaunchSettings
{
    private const string DotnetCommand = "dotnet";
    private const string ExecuteAssembly = "exec";
    private const string DependenciesOption = "--depsfile";
    private const string RuntimeOption = "--runtimeconfig";
    private const string DependenciesExtension = ".deps.json";
    private const string RuntimeExtension = ".runtimeconfig.json";
    private const string SolutionName = "KeyLoad.slnx";
    private const string RecoveryProcessProject = "KeyLoad.RecoveryTests.csproj";
    private const string TestsDirectory = "tests";
    private const string RecoveryProcessDirectory = "KeyLoad.RecoveryTests";
    private const string InspectorMode = "existing-store-inspect";
    private const string RepositoryMissingMessage = "The KeyLoad repository root is unavailable.";
    private const string RecoveryHostMissingMessage = "The referenced recovery process assembly is unavailable.";

    internal static (ProcessStartInfo StartInfo, string AssemblyPath) Create()
    {
        var root = FindRepositoryRoot();
        var assembly = typeof(CrashScenarioDispatcher).Assembly.Location;
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(RecoveryHostMissingMessage, assembly);
        }

        var start = new ProcessStartInfo(DotnetCommand)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        var host = typeof(ExistingStoreInspectorLaunchSettings).Assembly.Location;
        start.ArgumentList.Add(ExecuteAssembly);
        start.ArgumentList.Add(DependenciesOption);
        start.ArgumentList.Add(Path.ChangeExtension(host, DependenciesExtension));
        start.ArgumentList.Add(RuntimeOption);
        start.ArgumentList.Add(Path.ChangeExtension(host, RuntimeExtension));
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(CrashFixtureValues.ProcessMode);
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
                    && File.Exists(Path.Combine(directory.FullName, TestsDirectory, RecoveryProcessDirectory, RecoveryProcessProject)))
                {
                    return directory.FullName;
                }
            }
        }
        throw new DirectoryNotFoundException(RepositoryMissingMessage);
    }
}
