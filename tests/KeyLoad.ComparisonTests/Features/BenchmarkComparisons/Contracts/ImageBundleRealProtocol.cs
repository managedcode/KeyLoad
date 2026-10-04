namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ImageBundleRealProtocol
{
    internal const string Source = "sourceRevision";
    internal const string Archives = "archives";
    internal const string Server = "serverImage";
    internal const string Runner = "runnerImage";
    internal const string Node = "node";
    internal const string Solution = "KeyLoad.slnx";
    internal const string Scripts = "scripts";
    internal const string Features = "Features";
    internal const string Slice = "BenchmarkComparisons";
    internal const string Entry = "image-bundle-roundtrip.mjs";
    internal const string Failure = "The actual image bundle Node child failed its bound.";
    internal const int TimeoutMinutes = 15;
    internal const int CleanupSeconds = 10;
    internal const int MaximumOutputCharacters = 65_536;
    internal const int BufferCharacters = 4096;
    internal static IReadOnlyList<string> Assertions { get; } =
        ["nativeIdsAbsentBeforeImport", "nativeIdsExact", "manifestBytesExact", "buildReceiptBytesExact",
            "importReceiptBytesExact", "outputsExact", "cleanupCompleted"];

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Solution)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(Failure);
    }
}
