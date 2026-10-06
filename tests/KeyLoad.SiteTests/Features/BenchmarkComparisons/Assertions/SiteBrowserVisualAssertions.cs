namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserVisualAssertions
{
    public static async Task AssertResponsive(SiteBrowserChrome chrome, CancellationToken cancellationToken)
    {
        foreach (var width in SiteBrowserUiTokens.ViewportWidths)
        {
            await chrome.Cdp.CommandAsync(SiteBrowserTokens.SetDeviceMetrics, new Dictionary<string, object?>
            {
                [SiteBrowserTokens.WidthField] = width,
                [SiteBrowserTokens.HeightField] = SiteBrowserUiTokens.ViewportMobileHeight,
                [SiteBrowserTokens.DeviceScaleFactorField] = SiteBrowserTokens.One,
                [SiteBrowserTokens.MobileField] = false,
            }, cancellationToken);
            var overflow = await chrome.Cdp.EvaluateAsync(SiteBrowserUiTokens.OverflowScript, false, cancellationToken);
            await Assert.That(overflow.GetBoolean()).IsFalse();
            if (width <= SiteBrowserUiTokens.ViewportMobileWidth)
            {
                var region = await chrome.Cdp.EvaluateAsync(SiteBrowserUiTokens.TableScrollLabelScript, false, cancellationToken);
                await Assert.That(region.GetString()!.Contains(SiteBrowserUiTokens.ScrollLabelToken, StringComparison.OrdinalIgnoreCase)).IsTrue();
            }
        }
    }

    public static async Task AssertSceneLifecycle(SiteBrowserChrome chrome, string baseUrl,
        CancellationToken cancellationToken)
    {
        var cdp = chrome.Cdp;
        await chrome.NavigateAsync(SiteBrowserTokens.BlankUrl, cancellationToken);
        await chrome.NavigateAsync(baseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, cancellationToken);
        await SiteIsolatedBrowserAssertions.WaitAsync(cdp, cancellationToken);
        await AssertLazyScene(cdp, cancellationToken);
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, cancellationToken);
        var state = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneReadyScript, true, cancellationToken);
        var value = state.GetString();
        if (value != SiteBrowserUiTokens.StateReady)
        {
            throw new InvalidOperationException(value == SiteBrowserUiTokens.StateError
                ? SiteBrowserSceneTokens.BrowserSceneError : SiteBrowserSceneTokens.BrowserSceneUnsupported);
        }
        await AssertRendererState(cdp, cancellationToken);
        await SiteBrowserVectorAssetAssertions.AssertReadyMarkAsync(cdp, cancellationToken);
        await AssertMotionPlaysThenSettles(chrome, cancellationToken);
        await AssertReducedMotion(cdp, rendererReady: true, cancellationToken);
        await SiteBrowserVectorAssetAssertions.AssertRetinaResizeAsync(cdp, cancellationToken);
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollAwayScript, false, cancellationToken);
        var paused = await cdp.WaitForExpressionAsync(SiteBrowserUiTokens.ScenePausedScript, cancellationToken);
        await Assert.That(paused).IsTrue();
        await SiteBrowserVectorAssetAssertions.AssertPosterFallbackAsync(cdp, cancellationToken);

        await chrome.NavigateAsync(SiteBrowserTokens.BlankUrl, cancellationToken);
        await chrome.NavigateAsync(baseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, cancellationToken);
        await SiteIsolatedBrowserAssertions.WaitAsync(cdp, cancellationToken);
        await AssertLazyScene(cdp, cancellationToken);
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, cancellationToken);
        var restored = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneReadyScript, true, cancellationToken);
        await Assert.That(restored.GetString()).IsEqualTo(SiteBrowserUiTokens.StateReady);
        await AssertRendererState(cdp, cancellationToken);
        await SiteBrowserVectorAssetAssertions.AssertReadyMarkAsync(cdp, cancellationToken);
    }

    public static Task AssertSceneIsLazyBeforeHeroNavigation(SiteBrowserCdpClient cdp,
        CancellationToken cancellationToken) => AssertLazyScene(cdp, cancellationToken);

    private static async Task AssertLazyScene(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var atBenchmarkFragment = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneBenchmarkFragmentScript, false, cancellationToken);
        await Assert.That(atBenchmarkFragment.GetBoolean()).IsTrue();
        var state = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneIdleSnapshotScript, false, cancellationToken);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.SceneStateField).GetString()).IsEqualTo(
            SiteBrowserUiTokens.ScenePoster);
        await Assert.That(state.GetProperty(SiteBrowserUiTokens.CanvasCountField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await SiteBrowserVectorAssetAssertions.AssertPosterFallbackAsync(cdp, cancellationToken);
    }

    private static async Task AssertRendererState(SiteBrowserCdpClient cdp, CancellationToken cancellationToken)
    {
        var snapshot = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneSnapshotScript, false, cancellationToken);
        var backend = snapshot.GetProperty(SiteBrowserUiTokens.BackendField).GetString();
        await Assert.That(backend is SiteBrowserUiTokens.BackendWebGpu or SiteBrowserUiTokens.BackendWebGl).IsTrue();
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.CanvasCountField).GetInt32()).IsEqualTo(SiteBrowserTokens.One);
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.FrameField).GetString()).IsEqualTo(
            SiteBrowserUiTokens.FrameRendered);
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.BufferPixelsField).GetInt32() <= SiteBrowserUiTokens.RendererPixelLimit).IsTrue();
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.DrawCallsField).GetInt32() <= SiteBrowserUiTokens.RendererDrawCallLimit).IsTrue();
        await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.TrianglesField).GetInt32() <= SiteBrowserUiTokens.RendererTriangleLimit).IsTrue();
        var geometry = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneGeometrySnapshotScript, false, cancellationToken);
        var width = geometry.GetProperty(SiteBrowserUiTokens.CanvasWidthField).GetInt32();
        var height = geometry.GetProperty(SiteBrowserUiTokens.CanvasHeightField).GetInt32();
        await Assert.That(width > SiteTokens.Zero && height > SiteTokens.Zero).IsTrue();
        await Assert.That((long)width * height).IsEqualTo(geometry.GetProperty(SiteBrowserUiTokens.BufferPixelsField).GetInt32());
        await Assert.That(geometry.GetProperty(SiteBrowserUiTokens.PixelRatioField).GetDouble())
            .IsLessThanOrEqualTo(SiteBrowserUiTokens.RendererPixelRatioLimit);
    }

    /// <summary>
    /// AC-BC-012 as revised by the owner (2026-10-02): the scene moves as soon as it is ready at a constant draw budget,
    /// follows the pointer, and stops rendering once motion is paused.
    /// </summary>
    private static async Task AssertMotionPlaysThenSettles(SiteBrowserChrome chrome, CancellationToken cancellationToken)
    {
        var cdp = chrome.Cdp;
        var playing = await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionPlayingScript, false, cancellationToken);
        await Assert.That(playing.GetBoolean()).IsTrue();
        var originalMark = await SiteBrowserVectorAssetAssertions.ReadMarkAsync(cdp, cancellationToken);
        var point = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneCenterScript, false, cancellationToken);
        await cdp.CommandAsync(SiteBrowserTokens.InputDispatchMouseEvent, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.TypeField] = SiteBrowserTokens.MouseMoved,
            [SiteBrowserTokens.XField] = point.GetProperty(SiteBrowserTokens.XField).GetDouble(),
            [SiteBrowserTokens.YField] = point.GetProperty(SiteBrowserTokens.YField).GetDouble(),
        }, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneSettleWaitMilliseconds), TimeProvider.System, cancellationToken);
        await SiteBrowserVectorAssetAssertions.AssertPoseUpdatedAsync(cdp, originalMark, cancellationToken);
        var moving = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneGeometrySnapshotScript, false, cancellationToken);
        await Assert.That(moving.GetProperty(SiteBrowserUiTokens.FrameField).GetString())
            .IsEqualTo(SiteBrowserUiTokens.FrameRendered);
        await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneIdleWaitMilliseconds), TimeProvider.System, cancellationToken);
        var later = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneGeometrySnapshotScript, false, cancellationToken);
        await Assert.That(later.GetProperty(SiteBrowserUiTokens.RenderCallsField).GetInt32())
            .IsGreaterThan(moving.GetProperty(SiteBrowserUiTokens.RenderCallsField).GetInt32());
        await Assert.That(later.GetProperty(SiteBrowserUiTokens.DrawCallsField).GetInt32())
            .IsEqualTo(moving.GetProperty(SiteBrowserUiTokens.DrawCallsField).GetInt32());
        await Assert.That(later.GetProperty(SiteBrowserUiTokens.TrianglesField).GetInt32())
            .IsEqualTo(moving.GetProperty(SiteBrowserUiTokens.TrianglesField).GetInt32());
        var paused = await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionDisableScript, false, cancellationToken);
        await Assert.That(paused.GetBoolean()).IsTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneSettleWaitMilliseconds), TimeProvider.System, cancellationToken);
        var settled = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneGeometrySnapshotScript, false, cancellationToken);
        var metrics = settled.GetRawText();
        await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneIdleWaitMilliseconds), TimeProvider.System, cancellationToken);
        var idle = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneGeometrySnapshotScript, false, cancellationToken);
        await Assert.That(idle.GetRawText()).IsEqualTo(metrics);
        await Assert.That(idle.GetProperty(SiteBrowserUiTokens.RenderCallsField).GetInt32())
            .IsEqualTo(settled.GetProperty(SiteBrowserUiTokens.RenderCallsField).GetInt32());
    }

    private static async Task AssertReducedMotion(SiteBrowserCdpClient cdp, bool rendererReady,
        CancellationToken cancellationToken)
    {
        if (rendererReady)
        {
            var available = await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionAvailableScript, false, cancellationToken);
            if (available.GetBoolean())
            {
                await Assert.That((await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionEnableScript, false,
                    cancellationToken)).GetBoolean()).IsTrue();
                await Assert.That((await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionDisableScript, false,
                    cancellationToken)).GetBoolean()).IsTrue();
            }
        }

        await cdp.CommandAsync(SiteBrowserTokens.SetEmulatedMedia, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.FeaturesField] = new[]
            {
                new Dictionary<string, string>
                {
                    [SiteBrowserUiTokens.EmulationFeatureName] = SiteBrowserUiTokens.ReducedMotionFeature,
                    [SiteBrowserUiTokens.EmulationFeatureValue] = SiteBrowserUiTokens.ReducedMotionValue,
                },
            },
        }, cancellationToken);
        var reduced = await cdp.EvaluateAsync(SiteBrowserUiTokens.ReducedMotionScript, false, cancellationToken);
        await Assert.That(reduced.GetBoolean()).IsTrue();
        if (rendererReady)
        {
            var disabled = await cdp.WaitForExpressionAsync(SiteBrowserUiTokens.MotionDisabledScript, cancellationToken);
            await Assert.That(disabled).IsTrue();
        }
    }
}
