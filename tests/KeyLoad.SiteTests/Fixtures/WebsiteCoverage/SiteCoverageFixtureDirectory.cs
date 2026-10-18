using System.Text;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteCoverageFixtureDirectory : IAsyncDisposable
{
    private static readonly string TemporaryPrefix = Path.Combine(Path.GetTempPath(),
        SiteCoverageTokens.FixtureTemporaryDirectoryPrefix);

    public string Root { get; private set; } = string.Empty;

    public Task CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Root = Path.Combine(TemporaryPrefix, Guid.NewGuid().ToString(SiteCoverageTokens.CompactGuidFormat));
        Directory.CreateDirectory(Path.Combine(Root, FixtureRelativePathParts.Site, FixtureRelativePathParts.Features,
            FixtureRelativePathParts.BenchmarkComparisons));
        return Task.CompletedTask;
    }

    public async Task<byte[]> WriteSourceAsync(string relativePath, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Root, relativePath.Replace(SiteCoverageTokens.RelativeSeparator,
            Path.DirectorySeparatorChar));
        var bytes = new UTF8Encoding(false).GetBytes(text);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }

        return ValueTask.CompletedTask;
    }

    private static class FixtureRelativePathParts
    {
        public const string Site = "site";
        public const string Features = "Features";
        public const string BenchmarkComparisons = "BenchmarkComparisons";
    }
}
