namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonLiveProgressFile : IDisposable
{
    private const string DirectoryPrefix = "keyload-live-progress-";
    private const string FileName = "progress.log";
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid());

    internal ComparisonLiveProgressFile() => Directory.CreateDirectory(_directory);

    internal string Path => System.IO.Path.Combine(_directory, FileName);
    internal string MissingPath => System.IO.Path.Combine(_directory, Guid.NewGuid().ToString(), FileName);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
