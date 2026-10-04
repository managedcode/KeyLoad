using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedFailedCellBrowserTests
{
    private const string NumericField = "numeric";
    private const string ProgressField = "progress";
    private const string TotalProgressField = "totalProgress";
    private const string CorpusField = "corpus";
    /// <summary>AC-BC-FAIL-004: real Chrome renders controlled null-report copies without manufacturing measured values.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AC_BC_FAIL_004_RealChromeKeepsFailedJobVisibleAndUnavailable(bool all)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var browser = await SiteIsolatedBrowserStartup.StartAsync(fixture, token);
        await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
        var catalogPath = Path.Combine(browser.Output, SiteIsolatedFailureFixture.CatalogRelativePath);
        var projectionPath = Path.Combine(browser.Output, SiteIsolatedFailureFixture.ProjectionRelativePath);
        var catalogBytes = await File.ReadAllBytesAsync(catalogPath, token);
        var projectionBytes = await File.ReadAllBytesAsync(projectionPath, token);
        try
        {
            var copy = await SiteIsolatedFailureFixture.WriteBrowserCopyAsync(browser, all, token);
            using var projection = JsonDocument.Parse(copy.ToJsonString());
            var first = projection.RootElement.GetProperty(SiteIsolatedFields.Workers)[0];
            var selection = new SiteIsolatedSelection(first.GetProperty(SiteIsolatedFields.Scenario).GetString()!,
                first.GetProperty(SiteIsolatedFields.NodeCount).GetInt32(), "all", "throughput", "all");
            await browser.Chrome.NavigateAsync(browser.BaseUrl + SiteAssetTokens.IndexHtml, token);
            await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
            await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "scenario", selection.Scenario, token);
            await SiteIsolatedBrowserAssertions.SelectAsync(browser.Chrome.Cdp, "node-count", selection.NodeCount, token);
            await SiteIsolatedBrowserAssertions.AssertRowsAsync(browser.Chrome.Cdp, projection.RootElement, selection, token);
            await AssertUnavailableAsync(browser.Chrome.Cdp, first.GetProperty(SiteIsolatedFields.Id).GetString()!,
                first.GetProperty(SiteIsolatedFields.Target).GetString()!, all, token);
        }
        finally
        {
            await File.WriteAllBytesAsync(catalogPath, catalogBytes, token);
            await File.WriteAllBytesAsync(projectionPath, projectionBytes, token);
        }
        await browser.CompleteAsync(token);
    }

    private static async Task AssertUnavailableAsync(SiteBrowserCdpClient cdp, string id, string target, bool all, CancellationToken token)
    {
        const string script = """
            ((id,target)=>{
              const row=Array.from(document.querySelectorAll('#isolated-results tbody tr')).find(item=>item.dataset.workerId===id);
              const chart=Array.from(document.querySelectorAll('#isolated-results .isolated-chart li')).find(item=>item.textContent.startsWith(target+' ·'));
              const details=Array.from(document.querySelectorAll('#isolated-results details')).find(item=>item.dataset.workerId===id);
              return {numeric:[3,4,5,6].every(index=>row.cells[index].textContent==='Unavailable'),
                job:row.querySelector('a').textContent,progress:!!chart.querySelector('progress'),
                reason:details.textContent.includes('Benchmark failed; no measurement data is available.'),
                totalProgress:document.querySelectorAll('#isolated-results .isolated-chart progress').length,
                corpus:document.querySelector('#isolated-results').textContent.includes('corpus unavailable')};
            })
            """;
        var invocation = script + "(" + JsonSerializer.Serialize(id) + "," + JsonSerializer.Serialize(target) + ")";
        var value = await cdp.EvaluateAsync(invocation, false, token);
        await Assert.That(value.GetProperty(NumericField).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Job).GetString()).IsEqualTo("Failed benchmark job");
        await Assert.That(value.GetProperty(ProgressField).GetBoolean()).IsFalse();
        await Assert.That(value.GetProperty(SiteIsolatedFields.Reason).GetBoolean()).IsTrue();
        if (all)
        {
            await Assert.That(value.GetProperty(TotalProgressField).GetInt32()).IsEqualTo(0);
            await Assert.That(value.GetProperty(CorpusField).GetBoolean()).IsTrue();
        }
    }
}
