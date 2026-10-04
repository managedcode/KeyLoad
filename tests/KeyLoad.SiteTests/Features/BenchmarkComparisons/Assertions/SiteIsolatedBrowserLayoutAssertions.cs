using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserLayoutAssertions
{
    private const string DocumentContainedField = "documentContained";
    private const string WrapperContainedField = "wrapperContained";
    private const string RowsVisibleField = "rowsVisible";
    private const string WorkersVisibleField = "workersVisible";
    private const string FocusedField = "focused";
    private const string AccessibleField = "accessible";
    private const string ScrollableField = "scrollable";
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string OpenWorkers = "(()=>{document.querySelectorAll('#isolated-results .isolated-worker').forEach(e=>e.open=true);return true;})()";
    private const string Snapshot = """
        (()=>{
          const wrapper=document.querySelector('#isolated-results .isolated-table-scroll');
          const rect=wrapper.getBoundingClientRect();const width=document.documentElement.clientWidth;
          const visible=e=>getComputedStyle(e).display!=='none'&&getComputedStyle(e).visibility!=='hidden'&&e.getBoundingClientRect().height>0;
          const rows=Array.from(wrapper.querySelectorAll('tbody tr'));
          const workers=Array.from(document.querySelectorAll('#isolated-results .isolated-worker[open]'));
          wrapper.focus();
          return{documentContained:document.documentElement.scrollWidth<=width,
            wrapperContained:rect.width>0&&rect.left>=0&&rect.right<=width,
            rows:rows.length,rowsVisible:rows.every(row=>visible(row)&&row.cells[2].textContent.trim().length>0),
            workersVisible:workers.length===9&&workers.every(e=>visible(e.querySelector('dl'))),
            focused:document.activeElement===wrapper,
            accessible:wrapper.tabIndex===0&&wrapper.getAttribute('role')==='region'&&/scroll/i.test(wrapper.getAttribute('aria-label')??''),
            scrollable:['auto','scroll'].includes(getComputedStyle(wrapper).overflowX)&&wrapper.scrollWidth>wrapper.clientWidth};
        })()
        """;

    /// <summary>AC-ISO-009/011R: genuine Chrome retains nine native rows and long provenance at every supported viewport.</summary>
    public static async Task AssertAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        await cdp.EvaluateAsync(OpenWorkers, false, token);
        foreach (var width in SiteBrowserUiTokens.ViewportWidths)
        {
            await SetViewportAsync(cdp, width, token);
            var snapshot = await cdp.EvaluateAsync(Snapshot, false, token);
            foreach (var field in new[] { DocumentContainedField, WrapperContainedField, RowsVisibleField,
                WorkersVisibleField, FocusedField, AccessibleField })
            {
                await Assert.That(snapshot.GetProperty(field).GetBoolean()).IsTrue();
            }
            await Assert.That(snapshot.GetProperty(SiteIsolatedFields.Rows).GetInt32()).IsEqualTo(9);
            await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection,
                new("PointRead", 3, "all", "throughput", "all"), token);
            if (width <= SiteBrowserUiTokens.ViewportMobileWidth)
            {
                await Assert.That(snapshot.GetProperty(ScrollableField).GetBoolean()).IsTrue();
                await AssertKeyboardScrollAsync(cdp, token);
            }
        }
        await SetViewportAsync(cdp, SiteBrowserUiTokens.ViewportDesktopWidth, token);
    }

    private static Task<JsonElement> SetViewportAsync(SiteBrowserCdpClient cdp, int width, CancellationToken token)
        => cdp.CommandAsync(SiteBrowserTokens.SetDeviceMetrics, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.WidthField] = width,
            [SiteBrowserTokens.HeightField] = SiteBrowserUiTokens.ViewportMobileHeight,
            [SiteBrowserTokens.DeviceScaleFactorField] = SiteBrowserTokens.One,
            [SiteBrowserTokens.MobileField] = false,
        }, token);

    private static async Task AssertKeyboardScrollAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        const string reset = "(()=>{const e=document.querySelector('#isolated-results .isolated-table-scroll');e.scrollLeft=0;e.focus();return document.activeElement===e;})()";
        await Assert.That((await cdp.EvaluateAsync(reset, false, token)).GetBoolean()).IsTrue();
        foreach (var type in new[] { SiteBrowserTokens.KeyboardKeyDown, SiteBrowserTokens.KeyboardKeyUp })
        {
            await cdp.CommandAsync(SiteBrowserTokens.InputKeyDown, new Dictionary<string, object?>
            {
                [SiteBrowserTokens.TypeField] = type,
                [SiteBrowserTokens.KeyField] = SiteBrowserTokens.ArrowRight,
                [SiteBrowserTokens.CodeField] = SiteBrowserTokens.ArrowRight,
                [VirtualKeyField] = 39,
            }, token);
        }
        const string scrolled = "document.querySelector('#isolated-results .isolated-table-scroll').scrollLeft>0";
        await Assert.That(await cdp.WaitForExpressionAsync(scrolled, token)).IsTrue();
    }
}
