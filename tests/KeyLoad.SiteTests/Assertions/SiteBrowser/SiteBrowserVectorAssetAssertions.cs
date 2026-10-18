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
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkCountField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibilityField).GetString())
            .IsEqualTo(SiteVectorAssetTokens.HiddenVisibility);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.TransformField).GetString() is { Length: > 0 }).IsTrue();
        await AssertClusterGraphAsync(cdp, cancellationToken);
        await AssertCanvasDimensionsAsync(state);
    }

    private static async Task AssertClusterGraphAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        var graph = await cdp.EvaluateAsync(SiteBrowserSceneTokens.ClusterGraphScript, false, token);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.ModelsField).GetInt32()).IsEqualTo(SiteBrowserSceneTokens.ModelCount);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.AgentsField).GetInt32()).IsEqualTo(SiteBrowserSceneTokens.AgentCount);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.LinksField).GetInt32()).IsEqualTo(SiteBrowserSceneTokens.LinkCount);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.ClientsField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.MarksField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(graph.GetProperty(SiteBrowserSceneTokens.CaptionsField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        var entries = graph.GetProperty(SiteBrowserSceneTokens.ModelLabelsField).EnumerateArray().ToArray();
        await Assert.That(entries.Length).IsEqualTo(SiteBrowserSceneTokens.ModelNames.Length);
        foreach (var expected in SiteBrowserSceneTokens.ModelNames)
        {
            await Assert.That(entries.Count(label => string.Equals(label.GetProperty(SiteBrowserSceneTokens.TextField).GetString(),
                expected, StringComparison.Ordinal))).IsEqualTo(SiteTokens.One);
        }
        foreach (var label in entries)
        {
            await Assert.That(label.GetProperty(SiteBrowserSceneTokens.AccessibleField).GetBoolean()).IsTrue();
        }
        await AssertDescriptionAsync(graph);
    }

    private static async Task AssertDescriptionAsync(JsonElement state)
    {
        var description = state.GetProperty(SiteBrowserSceneTokens.DescriptionField).GetString()!;
        await Assert.That(description.Contains(SiteVectorAssetTokens.AgentDescription, StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(description.Contains(SiteVectorAssetTokens.DatabaseDescription, StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(description.Contains(SiteVectorAssetTokens.ConceptualDescription, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    public static async Task AssertPosterFallbackAsync(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken)
    {
        var state = await cdp.EvaluateAsync(SiteVectorAssetTokens.PosterStateScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.PosterVisibleField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.LoadedField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.SourceField).GetString()!
            .EndsWith(SiteContentTokens.DesktopPosterSuffix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.MarkCountField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteBrowserSceneTokens.LabelsHiddenField).GetBoolean()).IsTrue();
        await AssertDescriptionAsync(state);
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
        var previous = await ReadMarkAsync(cdp, cancellationToken);
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
            await AssertReadyMarkAsync(cdp, cancellationToken);
            var current = await ReadMarkAsync(cdp, cancellationToken);
            await Assert.That(current.GetProperty(SiteVectorAssetTokens.WidthField).GetDouble())
                .IsNotEqualTo(previous.GetProperty(SiteVectorAssetTokens.WidthField).GetDouble());
            previous = current;
        }
    }

    private static async Task AssertCanvasDimensionsAsync(JsonElement state)
    {
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.WidthField).GetDouble()).IsGreaterThan(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.HeightField).GetDouble()).IsGreaterThan(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteVectorAssetTokens.ContainedField).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.BufferPixelsField).GetInt32())
            .IsBetween(SiteTokens.One, SiteBrowserUiTokens.RendererPixelLimit);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.PixelRatioField).GetDouble())
            .IsLessThanOrEqualTo(SiteBrowserUiTokens.RendererPixelRatioLimit);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.TrianglesField).GetInt32())
            .IsBetween(SiteTokens.One, SiteBrowserUiTokens.RendererTriangleLimit);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.DrawCallsField).GetInt32())
            .IsBetween(SiteTokens.One, SiteBrowserUiTokens.RendererDrawCallLimit);
    }
}
