namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserFailureAssertions
{
    private const string Failed = "!document.querySelector('#isolated-error').hidden && !document.querySelector('#isolated-retry').hidden";
    private const string Cleared = """
        (()=>({rows:document.querySelectorAll('#isolated-results tbody tr').length,
          links:document.querySelectorAll('#isolated-results a').length,
          hosts:document.querySelectorAll('#isolated-results details').length,
          text:document.querySelector('#isolated-results').textContent,
          announcement:document.querySelector('#isolated-announcement').textContent}))()
        """;

    public static async Task AssertRetryAndClearingAsync(SiteBrowserSession browser, CancellationToken token)
    {
        var catalog = Path.Combine(browser.Output, "data", "isolated-catalog.json");
        var projection = Path.Combine(browser.Output, "data", "isolated", "projection.json");
        var catalogBytes = await File.ReadAllBytesAsync(catalog, token);
        var projectionBytes = await File.ReadAllBytesAsync(projection, token);
        try
        {
            await File.WriteAllTextAsync(catalog, "{invalid", token);
            await NavigateAndAssertFailureAsync(browser, token);
            await File.WriteAllBytesAsync(catalog, catalogBytes, token);
            await RetryAsync(browser.Chrome.Cdp, token);
            await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
            await File.AppendAllTextAsync(projection, " ", token);
            await NavigateAndAssertFailureAsync(browser, token);
            await File.WriteAllBytesAsync(projection, projectionBytes, token);
            // Two native click events overlap fetches; only the final owned generation may publish.
            await RetryAsync(browser.Chrome.Cdp, token);
            await SiteIsolatedBrowserAssertions.WaitAsync(browser.Chrome.Cdp, token);
        }
        finally
        {
            await File.WriteAllBytesAsync(catalog, catalogBytes, token);
            await File.WriteAllBytesAsync(projection, projectionBytes, token);
        }
    }

    private static async Task NavigateAndAssertFailureAsync(SiteBrowserSession browser, CancellationToken token)
    {
        await browser.Chrome.NavigateAsync(browser.BaseUrl + SiteAssetTokens.IndexHtml, token);
        await Assert.That(await browser.Chrome.Cdp.WaitForExpressionAsync(Failed, token)).IsTrue();
        var value = await browser.Chrome.Cdp.EvaluateAsync(Cleared, false, token);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Rows).GetInt32()).IsEqualTo(0);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Links).GetInt32()).IsEqualTo(0);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Hosts).GetInt32()).IsEqualTo(0);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Text).GetString()).IsEqualTo(string.Empty);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Announcement).GetString()).IsEqualTo(string.Empty);
    }

    private static async Task RetryAsync(SiteBrowserCdpClient cdp, CancellationToken token)
    {
        const string script = "(()=>{const button=document.querySelector('#isolated-retry');button.click();button.click();return true;})()";
        await Assert.That((await cdp.EvaluateAsync(script, false, token)).GetBoolean()).IsTrue();
    }
}
