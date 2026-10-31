using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedBrowserLayoutAssertions
{
    private const string DocumentContainedField = "documentContained";
    private const string WrapperContainedField = "wrapperContained";
    private const string RowsVisibleField = "rowsVisible";
    private const string WorkersVisibleField = "workersVisible";
    private const string FocusedField = "focused";
    private const string AccessibleField = "accessible";
    private const string ScrollableField = "scrollable";
    private const string ComparisonAccessibleField = "comparisonAccessible";
    private const string ComparisonContainedField = "comparisonContained";
    private const string ComparisonScrollableField = "comparisonScrollable";
    private const string ComparisonCellsField = "comparisonCells";
    private const string ComparisonSelector = "#isolated-results .comparison-scroll";
    private const string DetailsSelector = "#isolated-results .isolated-table-scroll";
    private const string VirtualKeyField = "windowsVirtualKeyCode";
    private const string OpenTable = "(()=>{document.querySelectorAll('#isolated-results .bench-details').forEach(e=>e.open=true);return true;})()";
    private const string Snapshot = """
        (()=>{
          const wrapper=document.querySelector('#isolated-results .isolated-table-scroll');
          const comparison=document.querySelector('#isolated-results .comparison-scroll');
          const mainRect=comparison.getBoundingClientRect();
          const rect=wrapper.getBoundingClientRect();const width=document.documentElement.clientWidth;
          const visible=e=>getComputedStyle(e).display!=='none'&&getComputedStyle(e).visibility!=='hidden'&&e.getBoundingClientRect().height>0;
          const rows=Array.from(wrapper.querySelectorAll('tbody tr'));
          const workers=Array.from(document.querySelectorAll('#isolated-results .ov-row'));
          wrapper.focus();
          return{documentContained:document.documentElement.scrollWidth<=width,
            wrapperContained:rect.width>0&&rect.left>=0&&rect.right<=width,
            comparisonContained:mainRect.width>0&&mainRect.left>=0&&mainRect.right<=width,
            comparisonAccessible:comparison.tabIndex===0&&comparison.getAttribute('role')==='region'&&/scroll/i.test(comparison.getAttribute('aria-label')??''),
            comparisonScrollable:['auto','scroll'].includes(getComputedStyle(comparison).overflowX)&&comparison.scrollWidth>comparison.clientWidth,
            comparisonCells:Array.from(comparison.querySelectorAll('td')).every(cell=>getComputedStyle(cell).minWidth==='0px'),
            rows:rows.length,rowsVisible:rows.every(row=>visible(row)&&row.cells[2].textContent.trim().length>0),
            workerCount:workers.length,workersVisible:workers.every(e=>visible(e)&&e.closest('.comparison-scroll')?.getBoundingClientRect().right<=width),
            focused:document.activeElement===wrapper,
            accessible:wrapper.tabIndex===0&&wrapper.getAttribute('role')==='region'&&/scroll/i.test(wrapper.getAttribute('aria-label')??''),
            scrollable:['auto','scroll'].includes(getComputedStyle(wrapper).overflowX)&&wrapper.scrollWidth>wrapper.clientWidth,
            width,left:rect.left,right:rect.right,scrollWidth:document.documentElement.scrollWidth};
        })()
        """;

    /// <summary>AC-ISO-009/011R: genuine Chrome retains every planned row and every engine at every supported viewport.</summary>
    public static async Task AssertAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, CancellationToken token)
    {
        await cdp.EvaluateAsync(OpenTable, false, token);
        foreach (var width in SiteBrowserUiTokens.ViewportWidths)
        {
            await SetViewportAsync(cdp, width, token);
            var snapshot = await cdp.EvaluateAsync(Snapshot, false, token);
            var fields = new[] { DocumentContainedField, WrapperContainedField, RowsVisibleField, WorkersVisibleField,
                FocusedField, AccessibleField, ComparisonAccessibleField, ComparisonContainedField, ComparisonCellsField };
            if (fields.Any(field => !snapshot.GetProperty(field).GetBoolean()))
            {
                TestContext.Current!.Output.WriteLine("Layout snapshot at " + width + ": " + snapshot.GetRawText());
            }
            foreach (var field in fields)
            {
                await Assert.That(snapshot.GetProperty(field).GetBoolean()).IsTrue();
            }
            await Assert.That(snapshot.GetProperty(SiteIsolatedFields.Rows).GetInt32()).IsEqualTo(expected.Planned(SiteIsolatedExpectedValues.All).Length);
            await Assert.That(snapshot.GetProperty(SiteIsolatedFields.WorkerCount).GetInt32()).IsEqualTo(expected.Targets(SiteIsolatedExpectedValues.All).Length);
            await SiteIsolatedBrowserAssertions.AssertTableAsync(cdp, expected, SiteIsolatedExpectedValues.All, token);
            if (width <= SiteBrowserUiTokens.ViewportMobileWidth)
            {
                await Assert.That(snapshot.GetProperty(ScrollableField).GetBoolean()).IsTrue();
                await Assert.That(snapshot.GetProperty(ComparisonScrollableField).GetBoolean()).IsTrue();
                await AssertKeyboardScrollAsync(cdp, DetailsSelector, token);
                await AssertKeyboardScrollAsync(cdp, ComparisonSelector, token);
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

    private static async Task AssertKeyboardScrollAsync(SiteBrowserCdpClient cdp, string selector, CancellationToken token)
    {
        var literal = JsonSerializer.Serialize(selector);
        var reset = "(()=>{const e=document.querySelector(" + literal + ");e.scrollLeft=0;e.focus();return document.activeElement===e;})()";
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
        var scrolled = "document.querySelector(" + literal + ").scrollLeft>0";
        await Assert.That(await cdp.WaitForExpressionAsync(scrolled, token)).IsTrue();
    }
}
