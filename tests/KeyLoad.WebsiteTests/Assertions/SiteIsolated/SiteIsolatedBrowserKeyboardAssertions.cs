using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedBrowserKeyboardAssertions
{
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string ArrowRightKey = "ArrowRight";
    private const string ArrowLeftKey = "ArrowLeft";
    private const string Focus = "(()=>{const e=document.querySelector('#isolated-node-count input:checked');e.focus();" +
        "return document.activeElement===e&&e.value==='1';})()";

    /// <summary>The cluster radio group follows native arrow-key selection and re-renders the owned rows.</summary>
    public static async Task AssertNodeSelectionAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        await Assert.That((await cdp.EvaluateAsync(Focus, false, token)).GetBoolean()).IsTrue();
        await KeyAsync(cdp, ArrowRightKey, 39, token);
        await AssertSelectedAsync(cdp, projection, 3, token);
        await KeyAsync(cdp, ArrowLeftKey, 37, token);
        await AssertSelectedAsync(cdp, projection, 1, token);
    }

    private static async Task AssertSelectedAsync(SiteBrowserCdpClient cdp, JsonElement projection, int nodes, CancellationToken token)
    {
        var selected = "(()=>{const e=document.querySelector('#isolated-node-count input:checked');" +
            "return e.value==='" + nodes + "'&&document.activeElement===e;})()";
        await Assert.That(await cdp.WaitForExpressionAsync(selected, token)).IsTrue();
        await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("sequential-read", nodes, "session", "throughput", "all"), token);
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
