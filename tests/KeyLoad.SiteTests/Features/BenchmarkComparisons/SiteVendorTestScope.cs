using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteVendorTestScope : IAsyncDisposable
{
    private SiteTempDirectory? _temporary;
    private readonly SiteTestInputs _sourceInputs;
    private SiteTestInputs? InputState { get; set; }
    private SiteIsolatedFixture? FixtureState { get; set; }

    private SiteVendorTestScope(SiteTestInputs sourceInputs)
    {
        _sourceInputs = sourceInputs;
    }

    internal SiteTestInputs Inputs => InputState ?? throw new InvalidOperationException(SiteVendorTokens.ScopeNotInitialized);
    internal SiteIsolatedFixture Fixture => FixtureState ?? throw new InvalidOperationException(SiteVendorTokens.ScopeNotInitialized);
    internal string Output => RequiredTemporary.Output;
    internal string FeatureSource => Path.Combine(Inputs.Repository, SiteVendorTokens.FeatureDirectory);

    internal static async Task<SiteVendorTestScope> CreateAsync(SiteIsolatedFixture fixture, CancellationToken token)
    {
        var inputs = fixture.Inputs.Site;
        var scope = new SiteVendorTestScope(inputs);
        try
        {
            scope.CreateTemporary();
            await CopyFeatureAsync(inputs, scope.FeatureSource, token);
            await CopyBuilderEntryAsync(inputs, scope.Inputs.Repository, token);
            await CopyRootAssetsAsync(inputs, scope.Inputs.Repository, token);
            await CopyTreeAsync(Path.Combine(inputs.Repository, SiteVendorTokens.AggregateScriptDirectory),
                Path.Combine(scope.Inputs.Repository, SiteVendorTokens.AggregateScriptDirectory), token);
            await CopyAggregateAsync(inputs, scope.Inputs.Aggregate, token);
            var isolatedInputs = new SiteIsolatedInputs(scope.Inputs, scope.Inputs.Aggregate);
            scope.FixtureState = new SiteIsolatedFixture(isolatedInputs, fixture.Root, fixture.Catalog, fixture.Projection);
            return scope;
        }
        catch (Exception)
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    internal async Task<JsonObject> ReadManifestAsync(CancellationToken token)
    {
        var path = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        return JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidOperationException(SiteVendorTokens.VendorError);
    }

    internal async Task WriteManifestAsync(JsonObject manifest, CancellationToken token)
    {
        var path = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        await File.WriteAllTextAsync(path, manifest.ToJsonString(), token);
    }

    internal Task<string> RunIndependentGzipOracleAsync(CancellationToken token)
        => SiteVendorGzipOracle.RunAsync(Inputs.Repository, FeatureSource, token);

    internal async Task AssertOriginalVendorUnchangedAsync(CancellationToken token)
    {
        foreach (var name in SiteVendorTokens.VendorFiles)
        {
            var original = Path.Combine(_sourceInputs.Repository, SiteVendorTokens.FeatureDirectory,
                SiteAssetTokens.ThreeVendorRelativePath, name);
            var emitted = Path.Combine(Output, SiteVendorTokens.FeatureOutputDirectory,
                SiteAssetTokens.ThreeVendorRelativePath, name);
            var expected = await File.ReadAllBytesAsync(original, token);
            var actual = await File.ReadAllBytesAsync(emitted, token);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }

    internal async Task AssertEmittedManifestMatchesCopyAsync(CancellationToken token)
    {
        var source = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        var emitted = Path.Combine(Output, SiteVendorTokens.FeatureOutputDirectory,
            SiteAssetTokens.ThreeVendorRelativePath, SiteAssetTokens.ThreeManifestFile);
        var sourceBytes = await File.ReadAllBytesAsync(source, token);
        var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
        await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
    }

    public ValueTask DisposeAsync()
    {
        var temporary = _temporary;
        return temporary is null ? ValueTask.CompletedTask : temporary.DisposeAsync();
    }

    private SiteTempDirectory RequiredTemporary => _temporary ??
        throw new InvalidOperationException(SiteVendorTokens.ScopeNotInitialized);

    private void CreateTemporary()
    {
        _temporary = SiteTempDirectory.Create();
        InputState = new(_temporary.Path, Path.Combine(_temporary.Path, SiteVendorTokens.AggregateDirectory), _sourceInputs.EvidenceRun,
            _sourceInputs.MeasuredRevision, _sourceInputs.SiteRevision);
    }

    private static async Task CopyAggregateAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(destination, "aggregate.json"),
            await File.ReadAllBytesAsync(Path.Combine(inputs.Aggregate, "aggregate.json"), token), token);
    }

    private static async Task CopyFeatureAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var source = Path.Combine(inputs.Repository, SiteVendorTokens.FeatureDirectory);
        await CopyDirectoryAsync(source, destination, token);
    }

    private static async Task CopyBuilderEntryAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var source = Path.Combine(inputs.Repository, SiteVendorTokens.BuildEntry);
        var target = Path.Combine(destination, SiteVendorTokens.BuildEntry);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(source, token), token);
    }

    private static async Task CopyRootAssetsAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var faviconSource = Path.Combine(inputs.Repository, SiteAssetTokens.FaviconSourcePath);
        var faviconTarget = Path.Combine(destination, SiteAssetTokens.FaviconSourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(faviconTarget)!);
        await File.WriteAllBytesAsync(faviconTarget, await File.ReadAllBytesAsync(faviconSource, token), token);
        foreach (var name in SiteVendorTokens.RootAssets)
        {
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.SiteRootDirectory, name);
            var target = Path.Combine(destination, SiteAssetTokens.SiteRootDirectory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(source, token), token);
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, SiteVendorTokens.SearchAllEntries,
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, SiteVendorTokens.SearchAllEntries, SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, token), token);
        }
    }

    private static async Task CopyTreeAsync(string source, string destination, CancellationToken token)
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
