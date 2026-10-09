using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBuildArtifacts
{
    private static readonly string[] FeatureAssets =
    [
        "isolated-contracts.mjs", "isolated-metadata.mjs", "isolated-metrics-validation.mjs",
        "isolated-report-validation.mjs", "isolated-http.mjs", "isolated-loader.mjs", "isolated-measurements.mjs",
        "isolated-view.mjs", "isolated-controls.mjs", "isolated-lab.mjs", SiteAssetTokens.ContractsModule,
        SiteAssetTokens.BootstrapModule, SiteAssetTokens.MeasurementModule, SiteAssetTokens.LoaderModule,
        SiteAssetTokens.SceneModule, SiteAssetTokens.GeometryModule, SiteAssetTokens.LifecycleModule,
        SiteAssetTokens.ObserversModule, SiteAssetTokens.Stylesheet, SiteAssetTokens.BrandStylesheet,
        SiteAssetTokens.TokenStylesheet, SiteAssetTokens.SceneStylesheet, SiteAssetTokens.PosterAsset,
    ];
    private static readonly string[] VendorFiles = [SiteAssetTokens.WebGpuVendorModule, SiteAssetTokens.CoreVendorModule,
        SiteAssetTokens.VendorLicense, SiteAssetTokens.ThreeManifestFile];
    private static readonly string[] GeneratedFiles = [SiteAssetTokens.NoJekyllFile, SiteAssetTokens.CnameFile,
        SiteAssetTokens.RobotsFile, SiteAssetTokens.SitemapFile];

    public static async Task CompareEmittedAssets(SiteTestInputs inputs, string output, CancellationToken token)
    {
        foreach (var relative in FeatureAssets)
        {
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath, relative);
            var emitted = Path.Combine(output, SiteAssetTokens.EmittedFeatureRelativePath, relative);
            await Assert.That(File.Exists(emitted)).IsTrue();
            var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
            var sourceBytes = await File.ReadAllBytesAsync(source, token);
            await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }

        foreach (var relative in VendorFiles)
        {
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath,
                SiteAssetTokens.ThreeVendorRelativePath, relative);
            var emitted = Path.Combine(output, SiteAssetTokens.EmittedFeatureRelativePath,
                SiteAssetTokens.ThreeVendorRelativePath, relative);
            await Assert.That(File.Exists(emitted)).IsTrue();
            var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
            var sourceBytes = await File.ReadAllBytesAsync(source, token);
            await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }

        var sourceFavicon = Path.Combine(inputs.Repository, SiteAssetTokens.FaviconSourcePath);
        var emittedFavicon = await File.ReadAllBytesAsync(Path.Combine(output, SiteAssetTokens.FaviconSvg), token);
        var originalFavicon = await File.ReadAllBytesAsync(sourceFavicon, token);
        await Assert.That(emittedFavicon.SequenceEqual(originalFavicon)).IsTrue();
        foreach (var file in GeneratedFiles)
        {
            await Assert.That(File.Exists(Path.Combine(output, file))).IsTrue();
        }
    }

    public static async Task CompareVendorManifest(SiteTestInputs inputs, string output, CancellationToken token)
    {
        var manifestPath = Path.Combine(output, SiteAssetTokens.EmittedFeatureRelativePath,
            SiteAssetTokens.ThreeVendorRelativePath, SiteAssetTokens.ThreeManifestFile);
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, token));
        foreach (var entry in manifest.RootElement.GetProperty(SiteAssetTokens.Files).EnumerateArray())
        {
            var relative = entry.GetProperty(SiteAssetTokens.Path).GetString()!;
            var source = Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath,
                SiteAssetTokens.ThreeVendorRelativePath, relative);
            var expected = entry.GetProperty(SiteTokens.Sha256).GetString();
            var actual = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source, token)));
            await Assert.That(actual).IsEqualTo(expected);
        }
    }
}
