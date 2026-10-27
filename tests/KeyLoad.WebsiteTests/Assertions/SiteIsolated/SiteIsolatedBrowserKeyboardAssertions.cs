namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedBrowserKeyboardAssertions
{
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string ArrowRightKey = "ArrowRight";
    private const string ArrowLeftKey = "ArrowLeft";
    private const string Focus = "(()=>{const e=document.querySelector('#isolated-type input:checked');e.focus();" +
        "return document.activeElement===e&&e.value==='all';})()";

    /// <summary>The family radio group follows native arrow-key selection and re-renders the owned view.</summary>
    public static async Task AssertTypeSelectionAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, CancellationToken token)
    {
        var next = SiteIsolatedExpectedValues.Families.First();
        await Assert.That((await cdp.EvaluateAsync(Focus, false, token)).GetBoolean()).IsTrue();
        await KeyAsync(cdp, ArrowRightKey, 39, token);
        await AssertSelectedAsync(cdp, next, token);
        await SiteIsolatedBrowserChartAssertions.AssertCardsAsync(cdp, expected, next, token);
        await KeyAsync(cdp, ArrowLeftKey, 37, token);
        await AssertSelectedAsync(cdp, SiteIsolatedExpectedValues.All, token);
        await SiteIsolatedBrowserChartAssertions.AssertOverviewAsync(cdp, expected, SiteIsolatedExpectedValues.Mixed, token);
    }

    private static async Task AssertSelectedAsync(SiteBrowserCdpClient cdp, string family, CancellationToken token)
    {
        var selected = "(()=>{const e=document.querySelector('#isolated-type input:checked');" +
            "return e.value===" + System.Text.Json.JsonSerializer.Serialize(family) + "&&document.activeElement===e;})()";
        await Assert.That(await cdp.WaitForExpressionAsync(selected, token)).IsTrue();
    }

    private static async Task KeyAsync(SiteBrowserCdpClient cdp, string key, int virtualKey, CancellationToken token)
    {
        foreach (var type in new[] { SiteBrowserTokens.KeyboardKeyDown, SiteBrowserTokens.KeyboardKeyUp })
        {
            await cdp.CommandAsync(SiteBrowserTokens.InputKeyDown, new Dictionary<string, object?>
            {
                [SiteBrowserTokens.TypeField] = type,
                [SiteBrowserTokens.KeyField] = key,
                [SiteBrowserTokens.CodeField] = key,
                [VirtualKeyField] = virtualKey,
            }, token);
        }
    }
}
