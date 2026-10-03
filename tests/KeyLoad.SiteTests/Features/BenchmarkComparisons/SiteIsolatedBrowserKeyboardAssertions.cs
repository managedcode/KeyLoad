using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserKeyboardAssertions
{
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string ArrowUpKey = "ArrowUp";
    private const string EnterKey = "Enter";

    public static async Task AssertNativeNodeSelectionAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        const string focus = "(()=>{const e=document.querySelector('#isolated-node-count');e.focus();return document.activeElement===e;})()";
        await Assert.That((await cdp.EvaluateAsync(focus, false, token)).GetBoolean()).IsTrue();
        await KeyAsync(cdp, ArrowUpKey, 38, token);
        await KeyAsync(cdp, EnterKey, 13, token);
        const string selected = "document.querySelector('#isolated-node-count').value==='2'";
        await Assert.That(await cdp.WaitForExpressionAsync(selected, token)).IsTrue();
        await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("PointRead", 2, "all", "throughput", "all"), token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "node-count", 3, token);
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
