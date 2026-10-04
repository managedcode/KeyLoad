namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Keeps the public site and the runtime console on one byte-identical KeyLoad brand source (ADR-053).</summary>
internal sealed class SiteBrandParityTests
{
    /// <summary>The site brand stylesheet and favicon mirror the console's canonical brand stylesheet and logo.</summary>
    [Test]
    public async Task AC_VI_001_SiteAndConsoleShareByteIdenticalBrandSources()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await AssertIdentical(Path.Combine(inputs.Repository, SiteAssetTokens.ConsoleBrandStylesheetPath),
            Path.Combine(inputs.Repository, SiteAssetTokens.FeatureRelativePath, SiteAssetTokens.BrandStylesheet), token);
        await AssertIdentical(Path.Combine(inputs.Repository, SiteAssetTokens.ConsoleLogoPath),
            Path.Combine(inputs.Repository, SiteAssetTokens.FaviconSourcePath), token);
    }

    private static async Task AssertIdentical(string canonical, string mirror, CancellationToken token)
    {
        await Assert.That(File.Exists(canonical)).IsTrue();
        await Assert.That(File.Exists(mirror)).IsTrue();
        var canonicalBytes = await File.ReadAllBytesAsync(canonical, token);
        var mirrorBytes = await File.ReadAllBytesAsync(mirror, token);
        await Assert.That(canonicalBytes.Length).IsGreaterThan(SiteTokens.Zero);
        await Assert.That(mirrorBytes.SequenceEqual(canonicalBytes)).IsTrue();
    }
}
