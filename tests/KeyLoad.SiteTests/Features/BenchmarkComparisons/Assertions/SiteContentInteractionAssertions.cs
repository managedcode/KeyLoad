namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentInteractionAssertions
{
    private const string OpenMenu = "document.querySelector('.nav-toggle').click(); document.getElementById('site-menu').matches(':popover-open')";
    private const string FollowProduct = "document.querySelector('#site-menu a[href=\"#product\"]').click(); document.getElementById('site-menu').matches(':popover-open')";
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
        await Assert.That((await cdp.EvaluateAsync(OpenMenu, false, token)).GetBoolean()).IsTrue();
        await Assert.That((await cdp.EvaluateAsync(FollowProduct, false, token)).GetBoolean()).IsFalse();
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
