using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedBrowserTests
{
    /// <summary>AC-ISO-009: actual emitted page shows every engine, test and topology with exact worker/artifact links.</summary>
    [Test]
    public async Task AC_ISO_009_RealChromeShowsEveryEngineTestAndTopology()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        var expected = new SiteIsolatedExpectedValues(projection.RootElement);
        await using var browser = await SiteIsolatedBrowserStartup.StartAsync(fixture, token);
        var cdp = browser.Chrome.Cdp;
        await SiteIsolatedBrowserAssertions.WaitAsync(cdp, token);
        await AssertAccessibilityAsync(cdp, expected, token);
        await SiteIsolatedBrowserChartAssertions.AssertOverviewAsync(cdp, expected, SiteIsolatedExpectedValues.Mixed, token);
        await SiteIsolatedBrowserAssertions.AssertTableAsync(cdp, expected, SiteIsolatedExpectedValues.All, token);
        foreach (var (key, _) in SiteIsolatedExpectedValues.General)
        {
            await SiteIsolatedBrowserChartAssertions.SortAsync(cdp, key, token);
            await SiteIsolatedBrowserChartAssertions.AssertOverviewAsync(cdp, expected, key, token);
        }
        await SiteIsolatedBrowserChartAssertions.SortAsync(cdp, SiteIsolatedExpectedValues.Mixed, token);
        await SiteIsolatedBrowserKeyboardAssertions.AssertTypeSelectionAsync(cdp, expected, token);
        foreach (var family in SiteIsolatedExpectedValues.Families)
        {
            await SiteIsolatedBrowserAssertions.SelectTypeAsync(cdp, family, token);
            await SiteIsolatedBrowserChartAssertions.AssertFamilyTableAsync(cdp, expected, family, token);
            await SiteIsolatedBrowserAssertions.AssertTableAsync(cdp, expected, family, token);
        }
        await SiteIsolatedBrowserAssertions.SelectTypeAsync(cdp, SiteIsolatedExpectedValues.All, token);
        await AssertEngineSelectionAsync(cdp, expected, token);
        await SiteIsolatedBrowserFailureAssertions.AssertRetryAndClearingAsync(browser, token);
        await browser.CompleteAsync(token);
    }

    private static async Task AssertEngineSelectionAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, CancellationToken token)
    {
        var target = expected.Targets(SiteIsolatedExpectedValues.All).First(expected.Measured);
        var family = SiteIsolatedExpectedValues.FamilyOf(target);
        var click = "(()=>{const b=Array.from(document.querySelectorAll('#isolated-results .ov-row')).find(row=>row.dataset.target===" +
            JsonSerializer.Serialize(target) + ")?.querySelector('.ov-name');if(!b)return false;b.click();" +
            "return document.querySelector('#isolated-type input:checked').value===" + JsonSerializer.Serialize(family) + ";})()";
        await SiteBrowserAssertions.EvaluateSuccess(cdp, click, token);
        await SiteIsolatedBrowserChartAssertions.AssertFamilyTableAsync(cdp, expected, family, token);
    }

    private static async Task AssertAccessibilityAsync(SiteBrowserCdpClient cdp, SiteIsolatedExpectedValues expected, CancellationToken token)
    {
        const string script = """
            (()=>{const group=document.querySelector('#isolated-type');
              return {labels:group.matches('fieldset')&&group.querySelector(':scope > legend').textContent.trim().length>0&&
                  group.querySelectorAll('input[type="radio"][name="isolated-type"]').length>0,
                status:document.querySelector('#isolated-announcement').getAttribute('aria-live'),
                alert:document.querySelector('#isolated-error').getAttribute('role'),
                caption:!!document.querySelector('#isolated-results .isolated-table-scroll table caption'),
                overview:document.querySelectorAll('#isolated-results .ov-row').length,
                samples:document.querySelectorAll('#isolated-results a[download]').length};})()
            """;
        var value = await cdp.EvaluateAsync(script, false, token);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Labels).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo("polite");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Alert).GetString()).IsEqualTo("alert");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Caption).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Overview).GetInt32())
            .IsEqualTo(expected.Targets(SiteIsolatedExpectedValues.All).Length);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Samples).GetInt32()).IsEqualTo(0);
    }
}
