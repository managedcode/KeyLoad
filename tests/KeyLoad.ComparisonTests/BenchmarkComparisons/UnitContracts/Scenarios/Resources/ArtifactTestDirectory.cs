namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ArtifactTestDirectory : IDisposable
{
    private const string Prefix = "keyload-execution-artifact-test-";
    private bool retainFailureEvidence;
    internal string Root { get; } = CreateOwnedDirectory();

    private static string CreateOwnedDirectory()
    {
        var path = Path.Combine(SessionNodeProcess.RepositoryRoot(), "artifacts", "qualification",
            "comparison-contract-fixtures", Prefix + Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return Resolve(directory);
    }

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    internal void RetainFailureEvidence() => retainFailureEvidence = true;

    public void Dispose()
    {
        if (!retainFailureEvidence)
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
