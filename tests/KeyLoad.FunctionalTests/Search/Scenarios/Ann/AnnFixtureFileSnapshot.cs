using KeyLoad.Server.Search;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnFixtureFileSnapshot
{
    private const int MaximumFiles = 16;
    private const int Excess = 1;
    private const long MaximumBytes = 2_097_152;
    private const string Pattern = "*.bin";
    private const string Invalid = "The real execution ANN fixture file snapshot exceeds its closed bound.";

    internal static (string Path, string Bytes)[] Capture(string directory)
    {
        var root = Path.Combine(directory, AnnProtocol.RootDirectory);
        var files = Directory.EnumerateFiles(root, Pattern, SearchOption.AllDirectories)
            .Take(MaximumFiles + Excess).Order(StringComparer.Ordinal).ToArray();
        if (files.Length > MaximumFiles)
        {
            throw new InvalidOperationException(Invalid);
        }
        long bytes = 0;
        return files.Select(file =>
        {
            bytes = checked(bytes + new FileInfo(file).Length);
            if (bytes > MaximumBytes)
            {
                throw new InvalidOperationException(Invalid);
            }
            return (Path.GetRelativePath(root, file), Convert.ToHexString(File.ReadAllBytes(file)));
        }).ToArray();
    }
}
