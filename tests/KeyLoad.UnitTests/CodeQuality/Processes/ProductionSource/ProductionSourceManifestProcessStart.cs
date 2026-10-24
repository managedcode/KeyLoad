using System.Diagnostics;

namespace KeyLoad.UnitTests.CodeQuality.Processes;

internal static class ProductionSourceManifestProcessStart
{
    private const string PowerShellCommand = "pwsh";
    private const string ScriptRelativePath = "scripts/Features/CodeQuality/functional-coverage.production-source-manifest.ps1";

    internal static string RepositoryRoot => FindRepositoryRoot();

    internal static ProcessStartInfo CreateStartInfo(string mode, string evidenceRoot)
    {
        var root = RepositoryRoot;
        var start = new ProcessStartInfo(PowerShellCommand)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, ScriptRelativePath));
        start.ArgumentList.Add("-Mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("-Root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("-EvidenceRoot");
        start.ArgumentList.Add(evidenceRoot);
        return start;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, ScriptRelativePath)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(ProductionSourceManifestProcess.Failure);
    }
}
