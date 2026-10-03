using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserVectorAssetAssertions
{
    public static async Task AssertReadyMarkAsync(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var state = await ReadMarkAsync(cdp, cancellationToken);
        await Assert.That(state.GetProperty("count").GetInt32()).IsEqualTo(SiteTokens.One);
        await Assert.That(state.GetProperty("loaded").GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty("source").GetString()).IsEqualTo("/favicon.svg");
        await Assert.That(state.GetProperty("display").GetString()).IsNotEqualTo(SiteVectorAssetTokens.CssNone);
        await Assert.That(state.GetProperty("visibility").GetString()).IsNotEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await Assert.That(CssNumber(state.GetProperty(SiteVectorAssetTokens.OpacityField).GetString()))
            .IsGreaterThan(SiteVectorAssetTokens.PositiveSize);
        await Assert.That(state.GetProperty("posterVisibility").GetString()).IsEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await AssertProjectedDimensions(state);
        var transform = state.GetProperty("transform").GetString() ?? string.Empty;
        await Assert.That(transform.StartsWith(SiteVectorAssetTokens.Matrix3dPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(transform.EndsWith(")", StringComparison.Ordinal)).IsTrue();
    }

    public static async Task AssertPosterFallbackAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteVectorAssetTokens.PosterStateScript, false, cancellationToken);
        await Assert.That(state.GetProperty("posterVisible").GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty("markVisible").GetBoolean()).IsFalse();
        await Assert.That(state.GetProperty("markCount").GetInt32()).IsEqualTo(SiteTokens.One);
    }

    public static async Task<JsonElement> ReadMarkAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken) =>
        await cdp.EvaluateAsync(SiteVectorAssetTokens.DataImageScript, false, cancellationToken);

    public static async Task AssertPoseUpdatedAsync(SiteBrowserCdpClient cdp, JsonElement previous,
        CancellationToken cancellationToken)
    {
        await AssertReadyMarkAsync(cdp, cancellationToken);
        var current = await ReadMarkAsync(cdp, cancellationToken);
        await Assert.That(current.GetProperty("transform").GetString())
            .IsNotEqualTo(previous.GetProperty("transform").GetString());
    }

    private static async Task AssertProjectedDimensions(JsonElement state)
    {
        var width = CssPixels(state.GetProperty("width").GetString());
        var height = CssPixels(state.GetProperty("height").GetString());
        await Assert.That(width > SiteVectorAssetTokens.PositiveSize && height > SiteVectorAssetTokens.PositiveSize).IsTrue();
        await Assert.That(state.GetProperty("offsetWidth").GetDouble()).IsEqualTo(width).Within(SiteVectorAssetTokens.CssDimensionTolerance);
        await Assert.That(state.GetProperty("offsetHeight").GetDouble()).IsEqualTo(height).Within(SiteVectorAssetTokens.CssDimensionTolerance);
        await Assert.That(state.GetProperty("rectWidth").GetDouble() > SiteVectorAssetTokens.PositiveSize).IsTrue();
        await Assert.That(state.GetProperty("rectHeight").GetDouble() > SiteVectorAssetTokens.PositiveSize).IsTrue();
    }

    private static double CssPixels(string? value) => value is not null &&
        value.EndsWith(SiteVectorAssetTokens.CssPixelSuffix, StringComparison.Ordinal) &&
        double.TryParse(value[..^SiteVectorAssetTokens.CssPixelSuffix.Length],
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pixels)
            ? pixels : SiteTokens.Zero;

    private static double CssNumber(string? value) => double.TryParse(value,
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
        ? number : SiteTokens.Zero;
}
