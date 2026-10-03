using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserVectorAssetAssertions
{
    public static async Task AssertReadyMarkAsync(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var state = await ReadMarkAsync(cdp, cancellationToken);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.CountField).GetInt32()).IsEqualTo(SiteTokens.One);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.LoadedField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.SourceField).GetString()).IsEqualTo(SiteVectorAssetTokens.ExpectedFaviconPath);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.DisplayField).GetString()).IsNotEqualTo(SiteVectorAssetTokens.CssNone);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.VisibilityField).GetString()).IsNotEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await Assert.That(CssNumber(state.GetProperty(SiteVectorAssetTokens.OpacityField).GetString()))
            .IsGreaterThan(SiteVectorAssetTokens.PositiveSize);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibilityField).GetString()).IsEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await AssertProjectedDimensions(state);
        var transform = state.GetProperty(SiteVectorAssetTokens.TransformField).GetString() ?? string.Empty;
        await Assert.That(transform.StartsWith(SiteVectorAssetTokens.Matrix3dPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(transform.EndsWith(SiteVectorAssetTokens.MatrixClosingCharacter)).IsTrue();
    }

    public static async Task AssertPosterFallbackAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteVectorAssetTokens.PosterStateScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibleField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkVisibleField).GetBoolean()).IsFalse();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkCountField).GetInt32()).IsEqualTo(SiteTokens.One);
    }

    public static async Task<JsonElement> ReadMarkAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken) =>
        await cdp.EvaluateAsync(SiteVectorAssetTokens.DataImageScript, false, cancellationToken);

    public static async Task AssertPoseUpdatedAsync(SiteBrowserCdpClient cdp, JsonElement previous,
        CancellationToken cancellationToken)
    {
        await AssertReadyMarkAsync(cdp, cancellationToken);
        var current = await ReadMarkAsync(cdp, cancellationToken);
        await Assert.That(current.GetProperty(SiteVectorAssetTokens.TransformField).GetString())
            .IsNotEqualTo(previous.GetProperty(SiteVectorAssetTokens.TransformField).GetString());
    }

    private static async Task AssertProjectedDimensions(JsonElement state)
    {
        var width = CssPixels(state.GetProperty(SiteVectorAssetTokens.WidthField).GetString());
        var height = CssPixels(state.GetProperty(SiteVectorAssetTokens.HeightField).GetString());
        await Assert.That(width > SiteVectorAssetTokens.PositiveSize && height > SiteVectorAssetTokens.PositiveSize).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.OffsetWidthField).GetDouble()).IsEqualTo(width).Within(SiteVectorAssetTokens.CssDimensionTolerance);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.OffsetHeightField).GetDouble()).IsEqualTo(height).Within(SiteVectorAssetTokens.CssDimensionTolerance);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.RectWidthField).GetDouble() > SiteVectorAssetTokens.PositiveSize).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.RectHeightField).GetDouble() > SiteVectorAssetTokens.PositiveSize).IsTrue();
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
