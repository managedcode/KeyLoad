namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteMetadataTestScope : IAsyncDisposable
{
    private SiteMetadataTestScope(string path, SiteIsolatedFixture fixture)
    {
        Repository = path;
        Fixture = fixture;
    }

    internal string Repository
    {
        get;
    }
    internal string Output => Repository + "-" + SiteMetadataTokens.OutputName;
    internal SiteIsolatedFixture Fixture
    {
        get;
    }

    internal static async Task<SiteMetadataTestScope> CreateAsync(SiteIsolatedFixture sourceFixture, CancellationToken token)
    {
        var inputs = sourceFixture.Inputs.Site;
        var path = Path.Combine(AppContext.BaseDirectory, SiteMetadataTokens.TestDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            await CopyTree(Path.Combine(inputs.Repository, SiteMetadataTokens.FeaturePath),
                Path.Combine(path, SiteMetadataTokens.FeaturePath), token);
            var aggregateScriptRoot = Path.Combine(path, SiteMetadataTokens.AggregateDirectoryPath);
            await CopyTree(Path.Combine(inputs.Repository, SiteMetadataTokens.AggregateDirectoryPath),
                aggregateScriptRoot, token);
            await CopyTree(Path.Combine(inputs.Repository, "site/scripts"), Path.Combine(path, "site/scripts"), token);
            await CopyFile(inputs.Repository, path, SiteMetadataTokens.ComparisonContractPath, token);
            await CopyFile(inputs.Repository, path, SiteMetadataTokens.FaviconSvgPath, token);
            var aggregate = Path.Combine(path, "isolated-capture", "aggregate.json");
            Directory.CreateDirectory(Path.GetDirectoryName(aggregate)!);
            await File.WriteAllBytesAsync(aggregate,
                await File.ReadAllBytesAsync(Path.Combine(inputs.Aggregate, "aggregate.json"), token), token);
            foreach (var asset in SiteMetadataTokens.IconAssetNames)
            {
                await CopyFile(inputs.Repository, path, Path.Combine(SiteMetadataTokens.SiteDirectory, asset), token);
            }
            var isolatedInputs = new SiteIsolatedInputs(new SiteTestInputs(path, Path.GetDirectoryName(aggregate)!,
                inputs.EvidenceRun, inputs.MeasuredRevision, inputs.SiteRevision), Path.GetDirectoryName(aggregate)!);
            var fixture = new SiteIsolatedFixture(isolatedInputs, sourceFixture.Root, sourceFixture.Catalog, sourceFixture.Projection);
            return new(path, fixture);
        }
        catch (Exception)
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Repository))
        {
            Directory.Delete(Repository, recursive: true);
        }
        if (Directory.Exists(Output))
        {
            Directory.Delete(Output, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    private static async Task CopyFile(string sourceRoot, string destinationRoot, string relative, CancellationToken token)
    {
        var source = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(source, token), token);
    }

    private static async Task CopyTree(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, token), token);
        }
    }
}
