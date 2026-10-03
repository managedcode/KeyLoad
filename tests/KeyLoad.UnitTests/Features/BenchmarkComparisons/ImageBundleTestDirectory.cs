namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ImageBundleTestDirectory : IDisposable
{
    private const string Prefix = "keyload-image-bundle-test-";
    internal string Root { get; } = Directory.CreateTempSubdirectory(Prefix).FullName;
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
