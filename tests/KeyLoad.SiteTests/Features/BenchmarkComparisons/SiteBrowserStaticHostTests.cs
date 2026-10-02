using System.Net;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserStaticHostTests
{
    [Test]
    public async Task AC_BC_025_StaticHostUsesBrowserMimeTypesAndSurvivesClientAbort()
    {
        await using var temporary = SiteTempDirectory.Create();
        Directory.CreateDirectory(temporary.Output);
        await File.WriteAllTextAsync(Path.Combine(temporary.Output, SiteBrowserTokens.ProbeHtmlFileName),
            SiteBrowserStaticHostTokens.HtmlBody);
        await File.WriteAllTextAsync(Path.Combine(temporary.Output, SiteBrowserTokens.ProbeStylesheetFileName),
            SiteBrowserStaticHostTokens.StyleBody);
        await File.WriteAllTextAsync(Path.Combine(temporary.Output, SiteBrowserTokens.ProbeModuleFileName),
            SiteBrowserStaticHostTokens.ModuleBody);
        await File.WriteAllBytesAsync(Path.Combine(temporary.Output, SiteBrowserTokens.AbortProbeFileName),
            new byte[checked((int)SiteBrowserTokens.ClientAbortTestFileBytes)]);
        await using var host = SiteStaticFileHost.Start(temporary.Output);
        var baseUri = new Uri(host.BaseUrl);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds),
        };

        await AssertContentType(client, baseUri, SiteBrowserTokens.ProbeHtmlFileName, SiteBrowserTokens.HtmlContentType);
        await AssertContentType(client, baseUri, SiteBrowserTokens.ProbeStylesheetFileName, SiteBrowserTokens.CssContentType);
        await AssertContentType(client, baseUri, SiteBrowserTokens.ProbeModuleFileName, SiteBrowserTokens.JavaScriptContentType);
        await AbortResponseAndFetchAgain(client, baseUri);
    }

    private static async Task AssertContentType(HttpClient client, Uri baseUri, string fileName, string expected)
    {
        using var response = await client.GetAsync(new Uri(baseUri, fileName), HttpCompletionOption.ResponseHeadersRead);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.ToString()).IsEqualTo(expected);
    }

    private static async Task AbortResponseAndFetchAgain(HttpClient client, Uri baseUri)
    {
        using (var response = await client.GetAsync(new Uri(baseUri, SiteBrowserTokens.AbortProbeFileName),
                   HttpCompletionOption.ResponseHeadersRead))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Content.Headers.ContentLength).IsEqualTo(SiteBrowserTokens.ClientAbortTestFileBytes);
        }

        using var recovered = await client.GetAsync(new Uri(baseUri, SiteBrowserTokens.ProbeHtmlFileName));
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await recovered.Content.ReadAsStringAsync()).IsEqualTo(SiteBrowserStaticHostTokens.HtmlBody);
    }
}

internal static class SiteBrowserStaticHostTokens
{
    public const string HtmlBody = "<!doctype html>";
    public const string StyleBody = "body{}";
    public const string ModuleBody = "export {};";
}
