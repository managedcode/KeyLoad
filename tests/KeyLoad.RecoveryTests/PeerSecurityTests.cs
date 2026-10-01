using System.Net;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Replication;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.RecoveryTests;

public sealed class PeerSecurityTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Request = request; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
    private static async Task<HttpRequestMessage> SignedRequest(PeerSecurity security)
    {
        var capture = new CaptureHandler(); var handler = (DelegatingHandler)security.CreateHandler();
        handler.InnerHandler!.Dispose(); handler.InnerHandler = capture;
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://node-a.test/internal/commands?mode=apply")
        { Content = new StringContent("{\"value\":1}") };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return request;
    }
    private static HttpRequest Incoming(HttpRequestMessage request, string host = "node-a.test", string query = "?mode=apply", string body = "{\"value\":1}")
    {
        var incoming = new DefaultHttpContext().Request;
        incoming.Method = "POST"; incoming.Scheme = "https"; incoming.Host = new HostString(host);
        incoming.Path = "/internal/commands"; incoming.QueryString = new QueryString(query);
        incoming.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        foreach (var header in request.Headers) incoming.Headers[header.Key] = header.Value.ToArray();
        return incoming;
    }
    [Fact]
    public async Task SignedPeerRequestRejectsReplayRecipientQueryAndBodyChanges()
    {
        var key = RandomNumberGenerator.GetBytes(32); var signed = await SignedRequest(new(key));
        var receiver = new PeerSecurity(key);
        Assert.True(await receiver.ValidateAsync(Incoming(signed), TestContext.Current.CancellationToken));
        Assert.False(await receiver.ValidateAsync(Incoming(signed), TestContext.Current.CancellationToken));
        Assert.False(await new PeerSecurity(key).ValidateAsync(Incoming(signed, host: "node-b.test"), TestContext.Current.CancellationToken));
        Assert.False(await new PeerSecurity(key).ValidateAsync(Incoming(signed, query: "?mode=delete"), TestContext.Current.CancellationToken));
        Assert.False(await new PeerSecurity(key).ValidateAsync(Incoming(signed, body: "{\"value\":2}"), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ExtremeTimestampIsRejectedWithoutOverflow()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-KeyLoad-Time"] = long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        request.Headers["X-KeyLoad-Nonce"] = Guid.NewGuid().ToString("N");
        request.Headers["X-KeyLoad-Signature"] = new string('0', 64);
        Assert.False(await new PeerSecurity(RandomNumberGenerator.GetBytes(32)).ValidateAsync(request, TestContext.Current.CancellationToken));
    }
}
