using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserKeyboardAssertions
{
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string ArrowUpKey = "ArrowUp";
    private const string EnterKey = "Enter";
    private const string ValueField = "value";
    private const string ActiveField = "active";
    private const string IndexField = "index";
    private const int MaximumSelectedValueCharacters = 8;
    private const int MaximumFailureEvidenceCharacters = 320;
    private const string SelectionSnapshot = "(()=>{const e=document.querySelector('#isolated-node-count');return{" +
        "active:!!e&&document.activeElement===e,value:typeof e?.value==='string'?e.value.slice(0,8):''," +
        "index:e?.selectedIndex??-1}})()";

    public static async Task AssertNativeNodeSelectionAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        const string focus = "(()=>{const e=document.querySelector('#isolated-node-count');e.focus();return document.activeElement===e;})()";
        await Assert.That((await cdp.EvaluateAsync(focus, false, token)).GetBoolean()).IsTrue();
        var arrowUp = await KeyAsync(cdp, ArrowUpKey, 38, token);
        KeyboardObservation? popupArrowUp = null;
        if (!arrowUp.Active && arrowUp.Value == "3" && arrowUp.Index == 2)
        {
            // Linux Chrome opens the native select popup before moving its highlighted option.
            popupArrowUp = await KeyAsync(cdp, ArrowUpKey, 38, token);
        }
        var enter = await KeyAsync(cdp, EnterKey, 13, token);
        const string selected = "document.querySelector('#isolated-node-count').value==='2'";
        var selectedExpectedValue = await cdp.WaitForExpressionAsync(selected, token);
        if (!selectedExpectedValue)
        { TestContext.Current!.Output.WriteLine(FailureEvidence(arrowUp, enter, popupArrowUp)); }
        await Assert.That(selectedExpectedValue).IsTrue();
        await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("PointRead", 2, "all", "throughput", "all"), token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "node-count", 3, token);
    }

    private static async Task<KeyboardObservation> KeyAsync(SiteBrowserCdpClient cdp, string key, int virtualKey,
        CancellationToken token)
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

        var state = await cdp.EvaluateAsync(SelectionSnapshot, false, token);
        var value = state.GetProperty(ValueField).GetString() ?? string.Empty;
        return new(key, DownAcknowledged: true, UpAcknowledged: true,
            state.GetProperty(ActiveField).GetBoolean(), value[..Math.Min(value.Length, MaximumSelectedValueCharacters)],
            state.GetProperty(IndexField).GetInt32());
    }

    private static string FailureEvidence(KeyboardObservation arrowUp, KeyboardObservation enter,
        KeyboardObservation? popupArrowUp)
    {
        var popup = popupArrowUp is null ? string.Empty : $"; popup:{Format(popupArrowUp)}";
        var detail = $"Native keyboard select failed; expected=2; {Format(arrowUp)}{popup}; {Format(enter)}";
        return detail[..Math.Min(detail.Length, MaximumFailureEvidenceCharacters)];
    }

    private static string Format(KeyboardObservation item) =>
        $"key={item.Key},downAck={item.DownAcknowledged},upAck={item.UpAcknowledged}," +
        $"active={item.Active},value={item.Value},index={item.Index}";

    private sealed record KeyboardObservation(string Key, bool DownAcknowledged, bool UpAcknowledged,
        bool Active, string Value, int Index);
}
