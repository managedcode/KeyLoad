using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteContentBrowserTests
{
    [Test]
    public async Task AC_BC_WEB_003_RealChromeShowsDesktopMobileSceneAndAccurateEmptyStateWithoutExternalRequests()
    {
        var inputs = SiteContentInputs.FromEnvironment();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteContentBuilderProcess.BuildAsync(inputs, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);

        await using var browser = await SiteContentBrowserStartup.StartAsync(inputs, temporary.Output, token);
        var cdp = browser.Chrome.Cdp;
        await Assert.That(await cdp.WaitForExpressionAsync(SiteContentBrowserTokens.ContentReadyPredicate, token)).IsTrue();
        await SiteBrowserVisualAssertions.AssertSceneIsLazyBeforeHeroNavigation(cdp, token);
        var desktop = await ReadContentStateAsync(browser.Chrome.Cdp, token);
        await AssertContentStateAsync(desktop, SiteContentTokens.DesktopPosterSuffix);
        var desktopRequests = await AssertLocalRequestsAsync(browser.Chrome.Cdp, browser.BaseUrl, token);
        await Assert.That(desktopRequests.Any(url => url.EndsWith(SiteContentTokens.DesktopPosterSuffix,
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(desktopRequests.Any(url => url.EndsWith(SiteAssetTokens.IndexHtml,
            StringComparison.Ordinal))).IsTrue();
        await AssertSceneLifecycleAsync(browser.Chrome, browser.BaseUrl, token);
        var sceneRequests = await AssertLocalRequestsAsync(cdp, browser.BaseUrl, token);
        await Assert.That(sceneRequests.Any(url => url.EndsWith(SiteAssetTokens.SceneModule,
            StringComparison.Ordinal))).IsTrue();

        await browser.Chrome.Cdp.CommandAsync(SiteBrowserTokens.SetDeviceMetrics, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.WidthField] = SiteContentBrowserTokens.MobileWidth,
            [SiteBrowserTokens.HeightField] = SiteContentBrowserTokens.MobileHeight,
            [SiteBrowserTokens.DeviceScaleFactorField] = SiteContentBrowserTokens.DeviceScaleFactor,
            [SiteBrowserTokens.MobileField] = true,
        }, token);
        await browser.Chrome.NavigateAsync(browser.BaseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, token);
        var mobile = await ReadContentStateAsync(browser.Chrome.Cdp, token);
        await AssertContentStateAsync(mobile, SiteContentTokens.MobilePosterSuffix);
        await Assert.That(mobile.GetProperty(SiteContentBrowserTokens.Mobile).GetBoolean()).IsTrue();
        var mobileRequests = await AssertLocalRequestsAsync(browser.Chrome.Cdp, browser.BaseUrl, token);
        await Assert.That(mobileRequests.Any(url => url.EndsWith(SiteContentTokens.MobilePosterSuffix,
            StringComparison.Ordinal))).IsTrue();
        await SiteContentInteractionAssertions.AssertNavigationAsync(cdp, token);
        await SiteContentInteractionAssertions.AssertClipboardUnavailableAsync(cdp, browser.BaseUrl, token);
        await browser.CompleteAsync(token);
    }

    private static async Task<JsonElement> ReadContentStateAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        const string script = """
            (()=>({title:document.title,empty:document.querySelector('#benchmarks .empty-state')?.textContent.trim(),
              catalog:document.querySelector('#benchmarks').hasAttribute('data-isolated-catalog'),
              rows:document.querySelectorAll('#benchmarks table tbody tr').length,
              controls:document.querySelectorAll('#benchmarks select, #benchmarks button.retry').length,
              scene:!!document.getElementById('cluster-scene'),
              status:document.querySelector('[data-scene-status]')?.textContent.trim(),
              poster:document.querySelector('.cluster-poster')?.currentSrc,
              mobile:matchMedia('(max-width: 760px)').matches}))()
            """;
        return await cdp.EvaluateAsync(script, awaitPromise: false, token);
    }

    private static async Task AssertContentStateAsync(JsonElement state, string posterSuffix)
    {
        await Assert.That(state.GetProperty(SiteContentTokens.TitleField).GetString()).IsEqualTo(SiteContentTokens.ProductTitle);
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Empty).GetString()).IsEqualTo(SiteContentTokens.EmptyState);
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Catalog).GetBoolean()).IsFalse();
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Rows).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Controls).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Scene).GetBoolean()).IsTrue();
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Status).GetString() is { Length: > 0 }).IsTrue();
        await Assert.That(state.GetProperty(SiteContentBrowserTokens.Poster).GetString()!
            .EndsWith(posterSuffix, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string[]> AssertLocalRequestsAsync(SiteBrowserCdpClient cdp, string baseUrl,
        CancellationToken token)
    {
        var events = cdp.DrainEvents();
        var requests = events.Where(item => item.TryGetProperty(SiteContentTokens.Event, out var method) &&
                method.GetString() == SiteContentTokens.NetworkRequestEvent)
            .Select(item => item.GetProperty(SiteContentTokens.Parameters).GetProperty(SiteContentTokens.Request)
                .GetProperty(SiteContentTokens.Url).GetString()!)
            .ToArray();
        var origin = new Uri(baseUrl).GetLeftPart(UriPartial.Authority);
        await Assert.That(requests.Length > SiteTokens.Zero).IsTrue();
        foreach (var request in requests)
        {
            if (!Uri.TryCreate(request, UriKind.Absolute, out var uri) || uri is null)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserProtocolFailure);
            }

            await Assert.That(uri.GetLeftPart(UriPartial.Authority)).IsEqualTo(origin);
            await Assert.That(uri.AbsolutePath.Contains(SiteContentTokens.DataDirectory, StringComparison.Ordinal)).IsFalse();
        }

        var resources = await cdp.EvaluateAsync("performance.getEntriesByType('resource').every(x => new URL(x.name).origin === location.origin)",
            awaitPromise: false, token);
        await Assert.That(resources.GetBoolean()).IsTrue();
        return requests;
    }

    private static async Task AssertSceneLifecycleAsync(SiteBrowserChrome chrome, string baseUrl,
        CancellationToken token)
    {
        var cdp = chrome.Cdp;
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, token);
        var state = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneReadyScript, true, token);
        await AssertSceneStateAsync(cdp, state.GetString(), token);

        await chrome.NavigateAsync(SiteBrowserTokens.BlankUrl, token);
        await chrome.NavigateAsync(baseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, token);
        await Assert.That(await cdp.WaitForExpressionAsync(SiteContentBrowserTokens.ContentReadyPredicate, token)).IsTrue();
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, token);
        var restored = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneReadyScript, true, token);
        await AssertSceneStateAsync(cdp, restored.GetString(), token);
        var empty = await ReadContentStateAsync(cdp, token);
        await Assert.That(empty.GetProperty(SiteContentBrowserTokens.Empty).GetString()).IsEqualTo(SiteContentTokens.EmptyState);
    }

    private static async Task AssertSceneStateAsync(SiteBrowserCdpClient cdp, string? state, CancellationToken token)
    {
        await Assert.That(state is SiteBrowserUiTokens.StateReady or SiteBrowserUiTokens.StateUnsupported).IsTrue();
        var snapshot = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneSnapshotScript, false, token);
        if (state == SiteBrowserUiTokens.StateReady)
        {
            await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.CanvasCountField).GetInt32())
                .IsEqualTo(SiteBrowserTokens.One);
        }
        else
        {
            await Assert.That(snapshot.GetProperty(SiteBrowserUiTokens.CanvasCountField).GetInt32())
                .IsEqualTo(SiteTokens.Zero);
            await SiteBrowserVectorAssetAssertions.AssertPosterFallbackAsync(cdp, token);
        }
    }
}

internal static class SiteContentBrowserTokens
{
    public const string ContentReadyPredicate = "document.readyState === 'complete' && document.querySelector('#benchmarks .empty-state') !== null";
    public const string Mobile = "mobile";
    public const string Empty = "empty";
    public const string Catalog = "catalog";
    public const string Rows = "rows";
    public const string Controls = "controls";
    public const string Scene = "scene";
    public const string Status = "status";
    public const string Poster = "poster";
    public const int MobileWidth = 390;
    public const int MobileHeight = 844;
    public const int DeviceScaleFactor = 1;
}
