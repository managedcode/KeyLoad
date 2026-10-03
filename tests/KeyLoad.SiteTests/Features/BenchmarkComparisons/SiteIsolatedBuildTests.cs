using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedBuildTests
{
    /// <summary>AC-ISO-008: standalone CLI emits hash-bound compact assets and preserves raw samples in GitHub only.</summary>
    [Test]
    public async Task AC_ISO_008_StandaloneBuildPreservesManifestAndExcludesRawWorkers()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var result = await SiteIsolatedBuilderProcess.RunAsync(fixture, temporary.Output, token);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        var catalogPath = Path.Combine(temporary.Output, "data", "isolated-catalog.json");
        var projectionPath = Path.Combine(temporary.Output, "data", "isolated", "projection.json");
        var manifestPath = Path.Combine(temporary.Output, "data", "isolated", "aggregate.json");
        var manifest = await File.ReadAllBytesAsync(manifestPath, token);
        var original = await File.ReadAllBytesAsync(Path.Combine(fixture.Inputs.Aggregate, "aggregate.json"), token);
        await Assert.That(manifest.AsSpan().SequenceEqual(original)).IsTrue();
        var projection = await File.ReadAllBytesAsync(projectionPath, token);
        var catalogBytes = await File.ReadAllBytesAsync(catalogPath, token);
        await Assert.That(projection.Length <= 4_194_304).IsTrue();
        await Assert.That(catalogBytes.Length <= 65_536).IsTrue();
        using var catalog = JsonDocument.Parse(catalogBytes);
        await Assert.That(catalog.RootElement.GetProperty(SiteIsolatedFields.Aggregate).GetProperty(SiteIsolatedFields.Sha256).GetString()).IsEqualTo(Hash(manifest));
        await Assert.That(catalog.RootElement.GetProperty(SiteIsolatedFields.Projection).GetProperty(SiteIsolatedFields.Sha256).GetString()).IsEqualTo(Hash(projection));
        var files = Directory.GetFiles(temporary.Output, "*", SearchOption.AllDirectories);
        await Assert.That(files.All(path => Path.GetFileName(path) is not ("worker.json" or "samples.csv"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(temporary.Output, "data", "runs"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(temporary.Output, "data", "catalog.json"))).IsFalse();
        var html = await File.ReadAllTextAsync(Path.Combine(temporary.Output, SiteAssetTokens.IndexHtml), token);
        await Assert.That(html.Contains("id=\"benchmarks\" class=\"benchmark-section\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains("data-isolated-catalog=\"./data/isolated-catalog.json\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(html.Contains("id=\"isolated-lab\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains("data-historical-unavailable", StringComparison.Ordinal)).IsFalse();
        await Assert.That(html.Contains("KEYLOAD_ISOLATED_EVIDENCE", StringComparison.Ordinal)).IsFalse();
        await SiteBuildArtifacts.CompareEmittedAssets(fixture.Inputs.Site, temporary.Output, token);
        await SiteBuildArtifacts.CompareVendorManifest(fixture.Inputs.Site, temporary.Output, token);
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = catalogPath,
            projection = projectionPath,
        }, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
