using System.Buffers.Binary;
using System.Net;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminFaviconAssertions
{
    private const string IcoPath = "/admin/favicon.ico";
    private const string Png32Path = "/admin/favicon-32x32.png";
    private const string Png96Path = "/admin/favicon-96x96.png";
    private const string TouchPath = "/admin/apple-touch-icon.png";
    private const string Icon192Path = "/admin/icon-192.png";
    private const string Icon512Path = "/admin/icon-512.png";
    private const string ConsoleRootPath = "/admin/";
    private const string HtmlMime = "text/html";
    private const string PngMime = "image/png";
    private const string IcoMime = "image/x-icon";
    private const string PngMagic = "89504E470D0A1A0A";
    private const string IcoMagic = "000001000300";
    private const string ContentPolicyHeader = "Content-Security-Policy";
    private const string NoSniffHeader = "X-Content-Type-Options";
    private const string ReferrerHeader = "Referrer-Policy";
    private const string ContentPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";
    private const string NoSniff = "nosniff";
    private const string NoReferrer = "no-referrer";
    private const string NoIndexMarkup = "<meta name=\"robots\" content=\"noindex,nofollow\">";
    private const string ThemeColorMarkup = "<meta name=\"theme-color\" content=\"#f7f6f2\">";
    private const string ConsoleFaviconSvgHref = "href=\"/admin/logo.svg\"";
    private const string ConsoleFaviconIcoHref = "href=\"/admin/favicon.ico\"";
    private const string ConsoleFavicon32Href = "href=\"/admin/favicon-32x32.png\"";
    private const string ConsoleFavicon96Href = "href=\"/admin/favicon-96x96.png\"";
    private const string ConsoleTouchHref = "href=\"/admin/apple-touch-icon.png\"";
    private const string CanonicalMarkup = "rel=\"canonical\"";
    private const string OpenGraphMarkup = "property=\"og:";
    private const string JsonLdMime = "application/ld+json";
    private const int IcoHeaderSize = 6;
    private const int IcoEntrySize = 16;
    private const int IcoEntryCount = 3;
    private const int IcoFrameCountOffset = 4;
    private const int IcoPayloadSizeOffset = 8;
    private const int IcoPayloadOffsetOffset = 12;
    private const int IcoDirectorySize = IcoHeaderSize + IcoEntrySize * IcoEntryCount;
    private const int IcoWidthOffset = 0;
    private const int IcoHeightOffset = 1;
    private const int PngSignatureLength = 8;
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;
    private const int PngDimensionLength = 4;

    private static readonly int[] IcoDimensions = [16, 32, 48];
    private static readonly (string Path, string Mime, int Dimension)[] PngResources =
    [
        (Png32Path, PngMime, 32), (Png96Path, PngMime, 96), (TouchPath, PngMime, 180),
        (Icon192Path, PngMime, 192), (Icon512Path, PngMime, 512),
    ];

    internal static async Task VerifyAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint);
        await VerifyConsoleHtml(http, cancellationToken);
        await VerifyIco(http, cancellationToken);
        foreach (var resource in PngResources)
        {
            await VerifyPng(http, resource, cancellationToken);
        }
        using var forbidden = await http.PostAsync(new Uri(IcoPath, UriKind.Relative), content: null, cancellationToken);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task VerifyConsoleHtml(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(ConsoleRootPath, UriKind.Relative), cancellationToken);
        await VerifyHeaders(response, HtmlMime);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        await Assert.That(html).Contains(NoIndexMarkup);
        await Assert.That(html).Contains(ThemeColorMarkup);
        await Assert.That(html.Contains(CanonicalMarkup, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(html.Contains(OpenGraphMarkup, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(html.Contains(JsonLdMime, StringComparison.OrdinalIgnoreCase)).IsFalse();
        foreach (var href in new[] { ConsoleFaviconSvgHref, ConsoleFaviconIcoHref, ConsoleFavicon32Href,
                     ConsoleFavicon96Href, ConsoleTouchHref })
        {
            await Assert.That(html.Contains(href, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task VerifyPng(HttpClient http, (string Path, string Mime, int Dimension) resource,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(resource.Path, UriKind.Relative), cancellationToken);
        await VerifyHeaders(response, resource.Mime);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await Assert.That(bytes.Length).IsGreaterThan(24);
        await Assert.That(Convert.ToHexString(bytes.AsSpan(0, 8))).IsEqualTo(PngMagic);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PngWidthOffset, PngDimensionLength)))
            .IsEqualTo(resource.Dimension);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(PngHeightOffset, PngDimensionLength)))
            .IsEqualTo(resource.Dimension);
        using var request = new HttpRequestMessage(HttpMethod.Head, resource.Path);
        using var head = await http.SendAsync(request, cancellationToken);
        await VerifyHeaders(head, resource.Mime);
        await Assert.That((await head.Content.ReadAsByteArrayAsync(cancellationToken)).Length).IsEqualTo(0);
    }

    private static async Task VerifyIco(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(IcoPath, UriKind.Relative), cancellationToken);
        await VerifyHeaders(response, IcoMime);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await Assert.That(bytes.Length >= IcoDirectorySize).IsTrue();
        await Assert.That(Convert.ToHexString(bytes.AsSpan(0, IcoHeaderSize))).IsEqualTo(IcoMagic);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(IcoFrameCountOffset, 2)))
            .IsEqualTo((ushort)IcoEntryCount);
        for (var frame = 0; frame < IcoEntryCount; frame++)
        {
            var entry = bytes.AsMemory(IcoHeaderSize + frame * IcoEntrySize, IcoEntrySize).ToArray();
            await Assert.That(entry[IcoWidthOffset]).IsEqualTo((byte)IcoDimensions[frame]);
            await Assert.That(entry[IcoHeightOffset]).IsEqualTo((byte)IcoDimensions[frame]);
            var frameSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(IcoPayloadSizeOffset, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(IcoPayloadOffsetOffset, 4));
            await Assert.That(offset >= IcoDirectorySize).IsTrue();
            await Assert.That(offset + frameSize <= bytes.Length).IsTrue();
            await Assert.That(frameSize > 0).IsTrue();
            var image = bytes.AsMemory((int)offset, (int)frameSize).ToArray();
            await Assert.That(Convert.ToHexString(image.AsSpan(0, PngSignatureLength))).IsEqualTo(PngMagic);
            await Assert.That(BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(PngWidthOffset, PngDimensionLength)))
                .IsEqualTo(IcoDimensions[frame]);
            await Assert.That(BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(PngHeightOffset, PngDimensionLength)))
                .IsEqualTo(IcoDimensions[frame]);
        }
        using var request = new HttpRequestMessage(HttpMethod.Head, IcoPath);
        using var head = await http.SendAsync(request, cancellationToken);
        await VerifyHeaders(head, IcoMime);
        await Assert.That((await head.Content.ReadAsByteArrayAsync(cancellationToken)).Length).IsEqualTo(0);
    }

    private static async Task VerifyHeaders(HttpResponseMessage response, string mime)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(mime);
        await Assert.That(response.Headers.CacheControl?.NoStore is true).IsTrue();
        await Assert.That(response.Headers.GetValues(ContentPolicyHeader).Single()).IsEqualTo(ContentPolicy);
        await Assert.That(response.Headers.GetValues(NoSniffHeader).Single()).IsEqualTo(NoSniff);
        await Assert.That(response.Headers.GetValues(ReferrerHeader).Single()).IsEqualTo(NoReferrer);
    }
}
