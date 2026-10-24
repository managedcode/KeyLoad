using System.Net;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.AdminDashboard;

internal static class AdminShellAssertions
{
    private const string EntryPath = "/admin";
    private const string RootPath = "/admin/";
    private const string ContentPolicyHeader = "Content-Security-Policy";
    private const string NoSniffHeader = "X-Content-Type-Options";
    private const string ReferrerHeader = "Referrer-Policy";
    private const string NoSniff = "nosniff";
    private const string NoReferrer = "no-referrer";
    private const string HtmlType = "text/html";
    private const string ContentPolicy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; "
        + "img-src 'self' data:; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";

    internal static async Task VerifyAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using var client = new HttpClient(handler)
        {
            BaseAddress = fixture.App.GetEndpoint(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint)
        };
        using var redirect = await client.GetAsync(new Uri(EntryPath, UriKind.Relative), cancellationToken);
        await Assert.That(redirect.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(redirect.Headers.Location?.OriginalString).IsEqualTo(RootPath);
        using var shell = await client.GetAsync(new Uri(RootPath, UriKind.Relative), cancellationToken);
        await VerifyHeadersAsync(shell);
        await Assert.That((await shell.Content.ReadAsByteArrayAsync(cancellationToken)).Length).IsGreaterThan(0);
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(RootPath, UriKind.Relative));
        using var head = await client.SendAsync(request, cancellationToken);
        await VerifyHeadersAsync(head);
        await Assert.That((await head.Content.ReadAsByteArrayAsync(cancellationToken)).Length).IsEqualTo(0);
        await VerifyFrameworkAsync(client, cancellationToken);
        await AdminFaviconAssertions.VerifyAsync(fixture, cancellationToken);
        using var forbidden = await client.PostAsync(new Uri(RootPath, UriKind.Relative), content: null, cancellationToken);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task VerifyFrameworkAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var script = await client.GetAsync(new Uri("/_framework/blazor.web.js", UriKind.Relative), cancellationToken);
        await Assert.That(script.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(script.Content.Headers.ContentType?.MediaType).IsEqualTo("text/javascript");
        await Assert.That((await script.Content.ReadAsByteArrayAsync(cancellationToken)).Length).IsGreaterThan(0);
    }

    private static async Task VerifyHeadersAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(HtmlType);
        await Assert.That(response.Headers.CacheControl?.NoStore is true).IsTrue();
        await Assert.That(response.Headers.GetValues(ContentPolicyHeader).Single()).IsEqualTo(ContentPolicy);
        await Assert.That(response.Headers.GetValues(NoSniffHeader).Single()).IsEqualTo(NoSniff);
        await Assert.That(response.Headers.GetValues(ReferrerHeader).Single()).IsEqualTo(NoReferrer);
    }
}
