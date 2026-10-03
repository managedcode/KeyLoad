using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedBrowserTests
{
    /// <summary>AC-ISO-009: actual emitted page exposes all native dimensions with exact worker/artifact links.</summary>
    [Test]
    public async Task AC_ISO_009_RealChromeSelectsEveryEngineNodeScenarioAndRepetition()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        await using var browser = await SiteBrowserSession.StartAsync(fixture.Inputs.Site, token);
        await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
        await AssertAccessibilityAsync(browser.Chrome.Cdp, token);
        await SiteIsolatedBrowserKeyboardAssertions.AssertNativeNodeSelectionAsync(browser.Chrome.Cdp, projection.RootElement, token);
        var workers = projection.RootElement.GetProperty(SiteIsolatedFields.Workers).EnumerateArray().ToArray();
        foreach (var scenario in workers.Select(worker => worker.GetProperty(SiteIsolatedFields.Scenario).GetString()!).Distinct(StringComparer.Ordinal))
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "scenario", scenario, token);
            foreach (var nodes in new[] { 1, 2, 3 })
            {
                await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "node-count", nodes, token);
                await SiteIsolatedBrowserAssertions.AssertRowsAsync(browser.Chrome.Cdp, projection.RootElement,
                    new(scenario, nodes, "all", "throughput", "all"), token);
            }
        }
        await AssertIndividualSelectionsAsync(browser.Chrome.Cdp, projection.RootElement, token);
        await SiteIsolatedBrowserFailureAssertions.AssertRetryAndClearingAsync(browser, token);
        await browser.CompleteAsync(token);
    }

    private static async Task AssertIndividualSelectionsAsync(SiteBrowserCdpClient cdp, JsonElement projection, CancellationToken token)
    {
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "scenario", "QueueCycle", token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "node-count", 3, token);
        foreach (var repetition in new object[] { "all", 0, 1, 2, 3, 4 })
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "repetition", repetition, token);
            foreach (var metric in new[] { "throughput", "p50", "p95", "p99", "errors", "enqueue", "receive", "ack", "cpu", "alloc", "rss" })
            {
                await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "metric", metric, token);
                await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("QueueCycle", 3, repetition, metric, "all"), token);
            }
        }
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "metric", "throughput", token);
        await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "repetition", "all", token);
        foreach (var target in projection.GetProperty(SiteIsolatedFields.Workers).EnumerateArray()
            .Select(worker => worker.GetProperty(SiteIsolatedFields.Target).GetString()!).Distinct(StringComparer.Ordinal))
        {
            await SiteIsolatedBrowserAssertions.SelectAsync(cdp, "target", target, token);
            await SiteIsolatedBrowserAssertions.AssertRowsAsync(cdp, projection, new("QueueCycle", 3, "all", "throughput", target), token);
        }
    }

    private static async Task AssertAccessibilityAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        const string script = """
            (()=>({labels:['scenario','node-count','target','metric','repetition'].every(id=>
              document.querySelector('label[for="isolated-'+id+'"]')),
              status:document.querySelector('#isolated-announcement').getAttribute('aria-live'),
              alert:document.querySelector('#isolated-error').getAttribute('role'),
              caption:!!document.querySelector('#isolated-results table caption'),
              provenance:document.querySelectorAll('#isolated-results details').length,
              samples:document.querySelectorAll('#isolated-results a[download]').length}))()
            """;
        var value = await cdp.EvaluateAsync(script, false, token);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Labels).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo("polite");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Alert).GetString()).IsEqualTo("alert");
        await Assert.That(value.GetProperty(SiteIsolatedFields.Caption).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Provenance).GetInt32()).IsEqualTo(9);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Samples).GetInt32()).IsEqualTo(0);
    }
}
