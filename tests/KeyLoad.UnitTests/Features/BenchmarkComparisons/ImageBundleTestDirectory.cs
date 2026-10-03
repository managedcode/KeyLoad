namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ImageBundleTestDirectory : IDisposable
{
    private const string Prefix = "keyload-image-bundle-test-";
    internal string Root { get; } = Resolve(Directory.CreateTempSubdirectory(Prefix));

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
