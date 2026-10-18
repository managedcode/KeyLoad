using System.Net;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteVectorAssetBrowserTests
{
    [Test]
    public async Task AC_VEC_003_RealRendererModule404KeepsOriginalArtworkAndNeverBecomesReady()
    {
        var inputs = SiteContentInputs.FromEnvironment();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteContentBuilderProcess.BuildAsync(inputs, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        await using var browser = await SiteContentBrowserStartup.StartAsync(inputs, temporary.Output, token);
        var cdp = browser.Chrome.Cdp;
        await browser.Chrome.NavigateAsync(SiteBrowserTokens.BlankUrl, token);
        var missingModule = Path.Combine(SiteAssetTokens.EmittedFeatureRelativePath,
            SiteAssetTokens.ThreeVendorRelativePath, SiteAssetTokens.WebGpuVendorModule);
        File.Delete(Path.Combine(temporary.Output, missingModule));
        await cdp.CommandAsync(SiteVectorAssetTokens.DisableCacheMethod, new Dictionary<string, object?>
        {
            [SiteVectorAssetTokens.CacheDisabledField] = true,
        }, token);
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds) };
        using var response = await http.GetAsync(new Uri(browser.BaseUrl + missingModule.Replace(Path.DirectorySeparatorChar, '/')), token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await browser.Chrome.NavigateAsync(browser.BaseUrl + SiteAssetTokens.IndexHtml, token);
        await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneScrollScript, false, token);
        await Assert.That(await cdp.WaitForExpressionAsync(SiteVectorAssetTokens.SceneErrorPredicate, token)).IsTrue();
        await SiteBrowserVectorAssetAssertions.AssertPosterFallbackAsync(cdp, token);
        var scene = await cdp.EvaluateAsync(SiteBrowserUiTokens.SceneIdleSnapshotScript, false, token);
        await Assert.That(scene.GetProperty(SiteBrowserUiTokens.CanvasCountField).GetInt32()).IsEqualTo(SiteTokens.Zero);
        await Assert.That((await cdp.EvaluateAsync(SiteBrowserUiTokens.MotionDisabledScript, false, token)).GetBoolean()).IsTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(SiteBrowserUiTokens.SceneIdleWaitMilliseconds), TimeProvider.System, token);
        foreach (var error in browser.Chrome.ReadErrors())
        {
            await Assert.That(error.Event).IsEqualTo(SiteBrowserUiTokens.ConsoleEvent);
            await Assert.That(error.Message.Contains(SiteVectorAssetTokens.NotFoundStatusMarker, StringComparison.Ordinal)).IsTrue();
        }

        await browser.CompleteAsync(token);
    }
}
