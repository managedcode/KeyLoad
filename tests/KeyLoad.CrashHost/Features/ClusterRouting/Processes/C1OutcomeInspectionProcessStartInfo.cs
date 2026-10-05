using System.Diagnostics;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal static class C1OutcomeInspectionProcessStartInfo
{
    private const string DotnetCommand = "dotnet";
    private const string ReleaseDirectory = "Release";
    private const string FrameworkDirectory = "net10.0";
    private const string TestsDirectory = "tests";
    private const string CrashHostDirectory = "KeyLoad.CrashHost";
    private const string AssemblyName = "KeyLoad.CrashHost.dll";
    private const string SolutionName = "KeyLoad.slnx";
    private const string ProjectName = "KeyLoad.CrashHost.csproj";
    private const string MissingHostMessage = "The Release CrashHost assembly is not built.";
    private const string RepositoryMissingMessage = "The KeyLoad repository root is unavailable.";

    internal static ProcessStartInfo Create()
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, TestsDirectory, CrashHostDirectory, "bin", ReleaseDirectory,
            FrameworkDirectory, AssemblyName);
        if (!File.Exists(assembly))
        { throw new FileNotFoundException(MissingHostMessage); }
        var start = new ProcessStartInfo(DotnetCommand)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(C1OutcomeInspectionProtocol.Mode);
        return start;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var origin in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var current = new DirectoryInfo(origin); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, SolutionName))
                    && File.Exists(Path.Combine(current.FullName, TestsDirectory, CrashHostDirectory, ProjectName)))
                { return current.FullName; }
            }
        }
        throw new DirectoryNotFoundException(RepositoryMissingMessage);
    }
}
