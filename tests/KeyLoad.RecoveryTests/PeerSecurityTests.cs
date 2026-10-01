using System.Net;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.IO.Pipelines;
using KeyLoad.Replication;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.RecoveryTests;

public sealed class PeerSecurityTests
{
    private sealed class OneShotContent(int length) : HttpContent
    {
        public int Serializations { get; private set; }
        protected override bool TryComputeLength(out long value) { value = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            if (++Serializations != 1) throw new InvalidOperationException("This payload can only be serialized once.");
            var buffer = new byte[16_384]; Array.Fill(buffer, (byte)'x');
            for (var remaining = length; remaining > 0; remaining -= Math.Min(remaining, buffer.Length))
                await stream.WriteAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), TestContext.Current.CancellationToken);
        }
    }
    private sealed class ValidateHandler(PeerSecurity receiver, long length) : HttpMessageHandler
    {
        public bool Called { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            Called = true;
            var incoming = new DefaultHttpContext().Request;
            incoming.Method = message.Method.Method; incoming.Scheme = message.RequestUri!.Scheme;
            incoming.Host = new(message.RequestUri.Authority); incoming.Path = message.RequestUri.AbsolutePath;
            incoming.ContentType = message.Content!.Headers.ContentType?.ToString();
            incoming.Body = await message.Content!.ReadAsStreamAsync(cancellationToken);
            foreach (var header in message.Headers) incoming.Headers[header.Key] = header.Value.ToArray();
            Assert.True(await receiver.ValidateAsync(incoming, cancellationToken));
            Assert.Equal(length, incoming.Body.Length); Assert.Equal(0, incoming.Body.Position);
            return new(HttpStatusCode.OK);
        }
    }
    [Fact]
    public async Task LargeSingleUsePayloadIsSerializedOnceAndVerifiedBeforeDispatch()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var receiver = new ValidateHandler(new(key), 10_000_000);
        var handler = (DelegatingHandler)new PeerSecurity(key).CreateHandler();
        handler.InnerHandler!.Dispose(); handler.InnerHandler = receiver;
        using var client = new HttpClient(handler);
        using var content = new OneShotContent(10_000_000);
        using var response = await client.PostAsync("https://node-a.test/raft", content, TestContext.Current.CancellationToken);
        Assert.True(receiver.Called); Assert.Equal(1, content.Serializations);
    }
    [Fact]
    public async Task OversizedSingleUsePayloadIsRejectedBeforeTransport()
    {
        var key = RandomNumberGenerator.GetBytes(32); var receiver = new ValidateHandler(new(key), 1_025);
        var handler = (DelegatingHandler)new PeerSecurity(key, 1_024).CreateHandler();
        handler.InnerHandler!.Dispose(); handler.InnerHandler = receiver;
        using var client = new HttpClient(handler); using var content = new OneShotContent(1_025);
        var failure = await Assert.ThrowsAsync<KeyLoadException>(() => client.PostAsync("https://node-a.test/raft", content, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCode.ResourceExhausted, failure.Code); Assert.False(receiver.Called);
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Request = request; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
    private static async Task<HttpRequestMessage> SignedRequest(PeerSecurity security, bool raft = false)
    {
        var capture = new CaptureHandler(); var handler = (DelegatingHandler)security.CreateHandler();
        handler.InnerHandler!.Dispose(); handler.InnerHandler = capture;
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://node-a.test/internal/commands?mode=apply")
        { Content = new StringContent("{\"value\":1}") };
        if (raft) { request.Headers.Add("X-Raft-Term", "7"); request.Headers.Add("X-Raft-Snapshot-Index", "100"); }
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return request;
    }
    private static HttpRequest Incoming(HttpRequestMessage request, string host = "node-a.test", string query = "?mode=apply", string body = "{\"value\":1}")
    {
        var incoming = new DefaultHttpContext().Request;
        incoming.Method = "POST"; incoming.Scheme = "https"; incoming.Host = new HostString(host);
        incoming.Path = "/internal/commands"; incoming.QueryString = new QueryString(query);
        incoming.ContentType = request.Content?.Headers.ContentType?.ToString();
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
    private sealed class OriginalPipe : Microsoft.AspNetCore.Http.Features.IRequestBodyPipeFeature
    {
        public PipeReader Reader { get; } = PipeReader.Create(Stream.Null);
    }
    [Fact]
    public async Task VerifiedBodyReaderReplaysTheAuthenticatedBodyInsteadOfTheConsumedNetworkPipe()
    {
        var key = RandomNumberGenerator.GetBytes(32); var signed = await SignedRequest(new(key));
        var request = Incoming(signed); var original = new OriginalPipe();
        request.HttpContext.Features.Set<Microsoft.AspNetCore.Http.Features.IRequestBodyPipeFeature>(original);
        Assert.True(await new PeerSecurity(key).ValidateAsync(request, TestContext.Current.CancellationToken));
        var read = await request.BodyReader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("{\"value\":1}", Encoding.UTF8.GetString(read.Buffer.ToArray()));
        request.BodyReader.AdvanceTo(read.Buffer.End); await request.BodyReader.CompleteAsync(); await original.Reader.CompleteAsync();
    }
    [Fact]
    public async Task ConsensusTermSnapshotIndexAndContentTypeCannotBeChangedOutsideTheSignature()
    {
        var key = RandomNumberGenerator.GetBytes(32); var signed = await SignedRequest(new(key), raft: true);
        Assert.True(await new PeerSecurity(key).ValidateAsync(Incoming(signed), TestContext.Current.CancellationToken));
        var term = Incoming(signed); term.Headers["X-Raft-Term"] = "8";
        Assert.False(await new PeerSecurity(key).ValidateAsync(term, TestContext.Current.CancellationToken));
        var index = Incoming(signed); index.Headers["X-Raft-Snapshot-Index"] = "101";
        Assert.False(await new PeerSecurity(key).ValidateAsync(index, TestContext.Current.CancellationToken));
        var type = Incoming(signed); type.ContentType = "application/octet-stream";
        Assert.False(await new PeerSecurity(key).ValidateAsync(type, TestContext.Current.CancellationToken));
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
