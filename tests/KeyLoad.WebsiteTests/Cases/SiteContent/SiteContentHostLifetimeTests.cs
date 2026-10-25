using System.Net;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteContentHostLifetimeTests
{
    [Test]
    public async Task IdleListenerClosureSettlesBeforeHealthyFollowingRequests()
    {
        await using var temporary = SiteTempDirectory.Create();
        Directory.CreateDirectory(temporary.Output);
        await File.WriteAllTextAsync(Path.Combine(temporary.Output, SiteBrowserTokens.ProbeHtmlFileName),
            SiteBrowserStaticHostTokens.HtmlBody);
        await CloseIdleHostAsync(temporary.Output);
        // The original pending accept must remain observed after the host and its awaiter are collected.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await using var following = SiteStaticFileHost.Start(temporary.Output);
        using var client = new HttpClient
        {
            Timeout = SiteExecutionConfiguration.Options.Value.BrowserCommandTimeout
        };
        using var response = await client.GetAsync(new Uri(new Uri(following.BaseUrl), SiteBrowserTokens.ProbeHtmlFileName));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(SiteBrowserStaticHostTokens.HtmlBody);
    }

    private static async Task CloseIdleHostAsync(string root)
    {
        await using var host = SiteStaticFileHost.Start(root);
        await host.DisposeAsync();
        await host.DisposeAsync();
    }
}
