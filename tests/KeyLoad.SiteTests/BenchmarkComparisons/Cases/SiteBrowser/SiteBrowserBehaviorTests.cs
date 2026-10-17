namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Runs the current isolated page and preserved hero scene in workflow-provided real Chrome.</summary>
internal sealed class SiteBrowserBehaviorTests
{
    /// <summary>AC-BC-025/ISO-009: the current comparison page preserves responsive navigation and the live scene lifecycle.</summary>
    [Test]
    public async Task CurrentPageRetainsResponsiveSceneLifecycleAndNativeCoverage()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var browser = await SiteIsolatedBrowserStartup.StartAsync(fixture, token);
        var cdp = browser.Chrome.Cdp;
        await SiteIsolatedBrowserAssertions.WaitAsync(cdp, token);
        await SiteBrowserVisualAssertions.AssertSceneIsLazyBeforeHeroNavigation(cdp, token);
        await SiteBrowserVisualAssertions.AssertResponsive(browser.Chrome, token);
        await SiteBrowserVisualAssertions.AssertSceneLifecycle(browser.Chrome, browser.BaseUrl, token);
        await browser.CompleteAsync(token);
        await SiteBrowserCoverageAssertions.AssertNativeConversionAsync(browser.BaseUrl, fixture.Inputs.Site, token);
    }
}
