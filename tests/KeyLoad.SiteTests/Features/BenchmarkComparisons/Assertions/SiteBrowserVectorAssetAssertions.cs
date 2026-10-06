using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserVectorAssetAssertions
{
    public static async Task AssertReadyMarkAsync(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        await Assert.That(await cdp.WaitForExpressionAsync(SiteBrowserSceneTokens.GraphReadyPredicate,
            cancellationToken)).IsTrue();
        var state = await ReadMarkAsync(cdp, cancellationToken);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.CountField).GetInt32()).IsEqualTo(SiteTokens.One);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.LoadedField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.SourceField).GetString()).IsEqualTo(SiteVectorAssetTokens.ExpectedFaviconPath);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.DisplayField).GetString()).IsNotEqualTo(SiteVectorAssetTokens.CssNone);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.VisibilityField).GetString()).IsNotEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await Assert.That(CssNumber(state.GetProperty(SiteVectorAssetTokens.OpacityField).GetString()))
            .IsGreaterThan(SiteVectorAssetTokens.PositiveSize);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibilityField).GetString()).IsEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await AssertClusterGraphAsync(cdp, cancellationToken);
        await AssertProjectedDimensions(state);
        var transform = state.GetProperty(SiteVectorAssetTokens.TransformField).GetString() ?? string.Empty;
        await Assert.That(transform.StartsWith(SiteVectorAssetTokens.Matrix3dPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(transform.EndsWith(SiteVectorAssetTokens.MatrixClosingCharacter)).IsTrue();
    }

    private static async Task AssertClusterGraphAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        var graph = await cdp.EvaluateAsync(SiteBrowserSceneTokens.ClusterGraphScript, false, token);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.SilosField).GetInt32()).IsEqualTo(3);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.GrainsField).GetInt32()).IsEqualTo(18);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.LinksField).GetInt32()).IsEqualTo(30);
        var description = graph.GetProperty(SiteBrowserSceneTokens.DescriptionField).GetString()!;
        await Assert.That(description.Contains("Orleans", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(description.Contains("conceptual", StringComparison.OrdinalIgnoreCase)).IsTrue();
        var labels = graph.GetProperty(SiteBrowserSceneTokens.LabelsField).EnumerateArray().ToArray();
        await Assert.That(labels.Length).IsEqualTo(3);
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            var text = label.GetProperty(SiteBrowserSceneTokens.TextField).GetString()!;
            var name = SiteBrowserSceneTokens.SiloNames[index];
            await Assert.That(text.Contains("Silo " + name, StringComparison.Ordinal)).IsTrue();
            await Assert.That(text.Contains("Node " + name, StringComparison.Ordinal)).IsTrue();
            await Assert.That(text.Contains("Grain activations", StringComparison.Ordinal)).IsTrue();
            await Assert.That(text.Contains("Node-local storage", StringComparison.Ordinal)).IsTrue();
            await Assert.That(label.GetProperty(SiteBrowserSceneTokens.VisibleField).GetBoolean()).IsTrue();
            await Assert.That(label.GetProperty(SiteBrowserSceneTokens.ContainedField).GetBoolean()).IsTrue();
        }
    }

    public static async Task AssertPosterFallbackAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteVectorAssetTokens.PosterStateScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibleField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkVisibleField).GetBoolean()).IsFalse();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkCountField).GetInt32()).IsEqualTo(SiteTokens.One);
        await Assert.That(state.GetProperty(SiteBrowserSceneTokens.LabelsHiddenField).GetBoolean()).IsTrue();
        var description = state.GetProperty(SiteBrowserSceneTokens.DescriptionField).GetString()!;
        await Assert.That(description.Contains("Orleans", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(description.Contains("conceptual", StringComparison.OrdinalIgnoreCase)).IsTrue();
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

    public static async Task AssertRetinaResizeAsync(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var originalMark = await ReadMarkAsync(cdp, cancellationToken);
        foreach (var width in SiteBrowserUiTokens.ViewportWidths)
        {
            await cdp.CommandAsync(SiteBrowserTokens.SetDeviceMetrics, new Dictionary<string, object?>
            {
                [SiteBrowserTokens.WidthField] = width,
                [SiteBrowserTokens.HeightField] = SiteBrowserUiTokens.ViewportMobileHeight,
                [SiteBrowserTokens.DeviceScaleFactorField] = SiteVectorAssetTokens.RetinaScale,
                [SiteBrowserTokens.MobileField] = false,
            }, cancellationToken);
            await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, cancellationToken);
            await Assert.That(await cdp.WaitForExpressionAsync(SiteVectorAssetTokens.ReadyPredicate, cancellationToken)).IsTrue();
            await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneSettleWaitMilliseconds), TimeProvider.System, cancellationToken);
            await AssertPoseUpdatedAsync(cdp, originalMark, cancellationToken);
            originalMark = await ReadMarkAsync(cdp, cancellationToken);
        }
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
        await AssertContainedRasterSize(state, width, height);
    }

    private static async Task AssertContainedRasterSize(JsonElement state, double width, double height)
    {
        var dimensions = state.GetProperty(SiteVectorAssetTokens.BoundsField);
        await Assert.That(dimensions.GetProperty(SiteVectorAssetTokens.ContainedField).GetBoolean()).IsTrue();
        await Assert.That(dimensions.GetProperty(SiteVectorAssetTokens.CenteredField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.RectWidthField).GetDouble() / width)
            .IsBetween(SiteVectorAssetTokens.MinimumProjectionRatio, SiteVectorAssetTokens.MaximumProjectionRatio);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.RectHeightField).GetDouble() / height)
            .IsBetween(SiteVectorAssetTokens.MinimumProjectionRatio, SiteVectorAssetTokens.MaximumProjectionRatio);
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
