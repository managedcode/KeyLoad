namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentInteractionAssertions
{
    private const string ToggleSelector = ".nav-toggle";
    private const string OpenMenu = "document.getElementById('site-menu').matches(':popover-open')";
    private const string ProductSelector = "#site-menu a[href=\"#product\"]";
    private const string ProductNavigationComplete = """
        (() => {
          const target = document.getElementById('product');
          const padding = parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop) || 0;
          const margin = parseFloat(getComputedStyle(target).scrollMarginTop) || 0;
          const root = document.scrollingElement;
          const desired = target.getBoundingClientRect().top + scrollY - padding - margin;
          const expected = Math.max(0, Math.min(root.scrollHeight - root.clientHeight, desired));
          return location.hash === '#product' && Math.abs(scrollY - expected) <= 1;
        })()
        """;
    private const string ProductNavigationDiagnostic = """
        (() => {
          const target = document.getElementById('product');
          return {hash:location.hash,scrollY,top:target.getBoundingClientRect().top,
            padding:getComputedStyle(document.documentElement).scrollPaddingTop,
            margin:getComputedStyle(target).scrollMarginTop,viewport:innerHeight,
            activeTag:document.activeElement?.tagName,activeId:document.activeElement?.id,
            scrollHeight:document.scrollingElement.scrollHeight,clientHeight:document.scrollingElement.clientHeight};
        })()
        """;
    private const string MenuClosed = "!document.getElementById('site-menu').matches(':popover-open')";
    private const string ClickCopy = "document.getElementById('copy-command').click(); true";
    private const string CopyUnavailable = "document.getElementById('copy-command').textContent === 'Source checkout'";
    private const string SetPermission = "Browser.setPermission";
    private const string Permission = "permission";
    private const string Name = "name";
    private const string ClipboardWrite = "clipboard-write";
    private const string Setting = "setting";
    private const string Denied = "denied";
    private const string Origin = "origin";

    public static async Task AssertNavigationAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        await SiteContentPointerInput.ClickAsync(cdp, ToggleSelector, token);
        await Assert.That((await cdp.EvaluateAsync(OpenMenu, false, token)).GetBoolean()).IsTrue();
        await SiteContentPointerInput.ClickAsync(cdp, ProductSelector, token);
        await Assert.That((await cdp.EvaluateAsync(OpenMenu, false, token)).GetBoolean()).IsFalse();
        var navigated = await cdp.WaitForExpressionAsync(ProductNavigationComplete, token);
        if (!navigated)
        {
            var diagnostic = await cdp.EvaluateAsync(ProductNavigationDiagnostic, false, token);
            await Assert.That(navigated).IsTrue().Because(diagnostic.GetRawText());
        }
        await SiteContentPointerInput.ClickAsync(cdp, ToggleSelector, token);
        await Assert.That((await cdp.EvaluateAsync(OpenMenu, false, token)).GetBoolean()).IsTrue();
        await cdp.CommandAsync(SiteBrowserTokens.SetDeviceMetrics, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.WidthField] = SiteBrowserUiTokens.ViewportDesktopWidth,
            [SiteBrowserTokens.HeightField] = SiteBrowserUiTokens.ViewportMobileHeight,
            [SiteBrowserTokens.DeviceScaleFactorField] = SiteBrowserTokens.One,
            [SiteBrowserTokens.MobileField] = false,
        }, token);
        await Assert.That(await cdp.WaitForExpressionAsync(MenuClosed, token)).IsTrue();
    }

    public static async Task AssertClipboardUnavailableAsync(SiteBrowserCdpClient cdp, string baseUrl,
        CancellationToken token)
    {
        await cdp.CommandAsync(SetPermission, new Dictionary<string, object?>
        {
            [Permission] = new Dictionary<string, object?> { [Name] = ClipboardWrite },
            [Setting] = Denied,
            [Origin] = new Uri(baseUrl).GetLeftPart(UriPartial.Authority),
        }, token);
        await cdp.EvaluateAsync(ClickCopy, false, token);
        await Assert.That(await cdp.WaitForExpressionAsync(CopyUnavailable, token)).IsTrue();
    }
}
