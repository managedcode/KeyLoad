using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedBrowserTests
{
    /// <summary>AC-ISO-009: actual emitted page exposes all execution dimensions with exact worker/artifact links.</summary>
    [Test]
    public async Task AC_ISO_009_RealChromeSelectsEveryEngineNodeScenarioAndRepetition()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        await using var browser = await SiteIsolatedBrowserStartup.StartAsync(fixture, token);
        await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
        await AssertAccessibilityAsync(browser.Chrome.Cdp, projection.RootElement, token);
        await SiteIsolatedBrowserKeyboardAssertions.AssertNodeSelectionAsync(browser.Chrome.Cdp, projection.RootElement, token);
        // The whole mixed case has no single request stream; the page offers its five concurrent lanes instead.
        foreach (var scenario in SiteIsolatedOracle.Cases.Where(item => item != "mixed"))
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "scenario", scenario, token);
            var metric = scenario is "index" or "verify-final" ? "duration" : "throughput";
            foreach (var nodes in new[] { 1, 3 })
            {
                await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "node-count", nodes, token);
                await SiteIsolatedBrowserAssertions.AssertRowsAsync(browser.Chrome.Cdp, projection.RootElement,
                    new(scenario, nodes, "session", metric, "all"), token);
            }
        }
        await AssertIndividualSelectionsAsync(browser.Chrome.Cdp, projection.RootElement, token);
        await AssertMatrixSelectionAsync(browser.Chrome.Cdp, projection.RootElement, token);
        await SiteIsolatedBrowserFailureAssertions.AssertRetryAndClearingAsync(browser, token);
        await browser.CompleteAsync(token);
    }

    private static async Task AssertIndividualSelectionsAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "scenario", "mixed/search", token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "node-count", 3, token);
        foreach (var metric in new[] { "throughput", "p50", "p95", "p99", "errors", "duration" })
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "metric", metric, token);
            await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("mixed/search", 3, "session", metric, "all"), token);
        }
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "metric", "throughput", token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "node-count", 1, token);
        const string types = "Array.from(document.querySelectorAll('#isolated-type .chip:not([hidden]) input')).map(input=>input.value)";
        var offered = (await cdp.EvaluateAsync(types, false, token)).EnumerateArray().Select(item => item.GetString()!).ToArray();
        await Assert.That(offered.First()).IsEqualTo("all");
        foreach (var type in offered.Skip(1).Append("all"))
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "type", type, token);
            await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("mixed/search", 1, "session", "throughput", type), token);
        }
    }

    private static async Task AssertMatrixSelectionAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        const string click = """
            (()=>{const button=document.querySelector('#isolated-results .matrix button[data-scenario="random-read"]');
              if(!button)return false;button.click();
              return document.querySelector('#isolated-scenario input:checked').value==='random-read';})()
            """;
        await SiteBrowserAssertions.EvaluateSuccess(cdp, click, token);
        await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("random-read", 1, "session", "throughput", "all"), token);
    }

    private static async Task AssertAccessibilityAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        const string script = """
            (()=>({labels:['scenario','node-count','metric','type'].every(id=>{
                const group=document.querySelector('#isolated-'+id);
                return group.matches('fieldset')&&group.querySelector(':scope > legend').textContent.trim().length>0&&
                  group.querySelectorAll('input[type="radio"][name="isolated-'+id+'"]').length>0;}),
              status:document.querySelector('#isolated-announcement').getAttribute('aria-live'),
              alert:document.querySelector('#isolated-error').getAttribute('role'),
              caption:!!document.querySelector('#isolated-results .isolated-table-scroll table caption'),
              provenance:document.querySelectorAll('#isolated-results .setup-card').length,
              samples:document.querySelectorAll('#isolated-results a[download]').length}))()
            """;
        var value = await cdp.EvaluateAsync(script, false, token);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Labels).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo("polite");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Alert).GetString()).IsEqualTo("alert");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Caption).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Provenance).GetInt32()).IsEqualTo(ProfiledWorkers(projection, 1));
        await Assert.That(value.GetProperty(SiteIsolatedFields.Samples).GetInt32()).IsEqualTo(0);
    }

    internal static int ProfiledWorkers(JsonElement projection, int nodeCount)
        => projection.GetProperty(SiteIsolatedFields.Workers).EnumerateArray()
            .Count(worker => worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32() == nodeCount
                && worker.GetProperty(SiteIsolatedFields.Session).GetProperty(SiteIsolatedFields.TargetProfile).ValueKind != JsonValueKind.Null);
}
