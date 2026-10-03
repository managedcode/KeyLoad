using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedStandaloneBrowserTests
{
    /// <summary>AC-ISO-008/009: actual standalone Chrome publication fetches compact270 evidence without legacy/raw preload.</summary>
    [Test]
    public async Task AC_ISO_009_StandaloneChromeLoadsOnlyCompactIsolatedEvidence()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        await using var browser = await SiteIsolatedBrowserStartup.StartAsync(fixture, token);
        await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
        await SiteIsolatedBrowserAssertions.AssertRowsAsync(browser.Chrome.Cdp, projection.RootElement,
            new("PointRead", 3, "all", "throughput", "all"), token);
        await SiteIsolatedBrowserLayoutAssertions.AssertAsync(browser.Chrome.Cdp, projection.RootElement, token);
        var hidden = await browser.Chrome.Cdp.EvaluateAsync("document.querySelector('#benchmarks').hidden", false, token);
        await Assert.That(hidden.GetBoolean()).IsTrue();
        const string resources = "performance.getEntriesByType('resource').map(entry=>entry.name)";
        var requests = (await browser.Chrome.Cdp.EvaluateAsync(resources, false, token)).EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        await Assert.That(requests.Any(url => url.EndsWith("/data/isolated-catalog.json", StringComparison.Ordinal))).IsTrue();
        await Assert.That(requests.Any(url => url.EndsWith("/data/isolated/projection.json", StringComparison.Ordinal))).IsTrue();
        await Assert.That(requests.Any(url => url.EndsWith("/data/catalog.json", StringComparison.Ordinal))).IsFalse();
        await Assert.That(requests.Any(url => url.Contains("/workers/", StringComparison.Ordinal) ||
            url.EndsWith("/samples.csv", StringComparison.Ordinal))).IsFalse();
        await SiteIsolatedBrowserFailureAssertions.AssertRetryAndClearingAsync(browser, token);
        await browser.CompleteAsync(token);
    }
}
