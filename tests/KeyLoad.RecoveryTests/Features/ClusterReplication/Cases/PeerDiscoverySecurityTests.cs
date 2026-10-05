using KeyLoad.Replication;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-001/005/006: actual signed discovery requests reject every route, body and authentication mutation.</summary>
internal sealed class PeerDiscoverySecurityTests
{
    private const string RelativePath = "internal/silo";
    private const string Fragment = "#fragment";
    private const string UserInfoOrigin = "https://user@node-a.test:8080";
    private const string InvalidHexSignature = "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";
    private const string InvalidLength = "invalid";

    /// <summary>Signing copies the credential once, replaces existing authentication headers and rejects replay.</summary>
    [Test]
    public async Task GenuineSignUsesCopiedCredentialAndReplayIsRejected()
    {
        using var fixture = new PeerDiscoveryFixture();
        Array.Clear(fixture.Secret);
        using var message = fixture.Sign();
        var oldNonce = message.Headers.GetValues(PeerDiscoveryProtocol.NonceHeader).Single();
        fixture.Sender.Sign(message);
        await Assert.That(message.Headers.GetValues(PeerDiscoveryProtocol.NonceHeader).Count()).IsEqualTo(1);
        await Assert.That(message.Headers.GetValues(PeerDiscoveryProtocol.NonceHeader).Single() == oldNonce).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation)).IsTrue();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation)).IsFalse();
    }

    /// <summary>A malformed or forged actual request cannot consume a one-slot nonce capacity.</summary>
    [Test]
    [Arguments(PeerDiscoveryMutation.Time)]
    [Arguments(PeerDiscoveryMutation.Nonce)]
    [Arguments(PeerDiscoveryMutation.Signature)]
    [Arguments(PeerDiscoveryMutation.Method)]
    [Arguments(PeerDiscoveryMutation.Path)]
    [Arguments(PeerDiscoveryMutation.PathBase)]
    [Arguments(PeerDiscoveryMutation.RawPath)]
    [Arguments(PeerDiscoveryMutation.Query)]
    [Arguments(PeerDiscoveryMutation.Recipient)]
    [Arguments(PeerDiscoveryMutation.ContentType)]
    [Arguments(PeerDiscoveryMutation.FramedBody)]
    [Arguments(PeerDiscoveryMutation.UnframedBody)]
    [Arguments(PeerDiscoveryMutation.TransferEncoding)]
    public async Task RejectedRequestDoesNotConsumeNonceAdmission(PeerDiscoveryMutation mutation)
    {
        using var fixture = new PeerDiscoveryFixture();
        using var signed = fixture.Sign();
        var changed = PeerDiscoveryFixture.Incoming(signed);
        fixture.Mutate(changed, mutation);
        await Assert.That(await fixture.Receiver.ValidateAsync(changed, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(changed, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation)).IsTrue();
    }

    /// <summary>Duplicate authentication values are invalid even when every individual value is otherwise genuine.</summary>
    [Test]
    [Arguments(PeerDiscoveryProtocol.TimeHeader)]
    [Arguments(PeerDiscoveryProtocol.NonceHeader)]
    [Arguments(PeerDiscoveryProtocol.SignatureHeader)]
    public async Task DuplicateAuthenticationHeadersDoNotConsumeCapacity(string header)
    {
        using var fixture = new PeerDiscoveryFixture();
        using var signed = fixture.Sign();
        var changed = PeerDiscoveryFixture.Incoming(signed);
        var value = changed.Headers[header].Single()!;
        changed.Headers[header] = new StringValues([value, value]);
        await Assert.That(await fixture.Receiver.ValidateAsync(changed, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation)).IsTrue();
    }

    /// <summary>Missing authentication headers remain invalid without spending a slot.</summary>
    [Test]
    [Arguments(PeerDiscoveryProtocol.TimeHeader)]
    [Arguments(PeerDiscoveryProtocol.NonceHeader)]
    [Arguments(PeerDiscoveryProtocol.SignatureHeader)]
    public async Task MissingAuthenticationHeadersDoNotConsumeCapacity(string header)
    {
        using var fixture = new PeerDiscoveryFixture();
        using var signed = fixture.Sign();
        var changed = PeerDiscoveryFixture.Incoming(signed);
        changed.Headers.Remove(header);
        await Assert.That(await fixture.Receiver.ValidateAsync(changed, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation)).IsTrue();
    }

    /// <summary>Malformed hexadecimal and an empty GUID nonce are rejected before authentication admission.</summary>
    [Test]
    public async Task MalformedSignatureAndEmptyNonceDoNotConsumeCapacity()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var signed = fixture.Sign();
        var signature = PeerDiscoveryFixture.Incoming(signed);
        signature.Headers[PeerDiscoveryProtocol.SignatureHeader] = InvalidHexSignature;
        var nonce = PeerDiscoveryFixture.Incoming(signed);
        nonce.Headers[PeerDiscoveryProtocol.NonceHeader] = Guid.Empty.ToString(PeerDiscoveryProtocol.NonceFormat);
        var length = PeerDiscoveryFixture.Incoming(signed);
        length.Headers[HeaderNames.ContentLength] = InvalidLength;
        await Assert.That(await fixture.Receiver.ValidateAsync(signature, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(nonce, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(length, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation)).IsTrue();
    }

    /// <summary>The public signing boundary rejects generic routes, recipients, framing and even empty HttpContent.</summary>
    [Test]
    public async Task OutboundDiscoveryContractErrorsAreTypedBeforeTransport()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var post = new HttpRequestMessage(HttpMethod.Post, PeerDiscoveryFixture.Origin + ReplicaProtocol.DiscoveryPath);
        using var relative = new HttpRequestMessage(HttpMethod.Get, new Uri(RelativePath, UriKind.Relative));
        using var route = new HttpRequestMessage(HttpMethod.Get, PeerDiscoveryFixture.Origin + PeerDiscoveryFixture.OtherPath);
        using var query = new HttpRequestMessage(HttpMethod.Get, PeerDiscoveryFixture.Origin + ReplicaProtocol.DiscoveryPath + PeerDiscoveryFixture.Query);
        using var fragment = new HttpRequestMessage(HttpMethod.Get, PeerDiscoveryFixture.Origin + ReplicaProtocol.DiscoveryPath + Fragment);
        using var userInfo = new HttpRequestMessage(HttpMethod.Get, UserInfoOrigin + ReplicaProtocol.DiscoveryPath);
        using var content = new HttpRequestMessage(HttpMethod.Get, PeerDiscoveryFixture.Origin + ReplicaProtocol.DiscoveryPath)
        { Content = new ByteArrayContent([]) };
        using var host = fixture.Sign();
        host.Headers.Host = PeerDiscoveryFixture.OtherHost;
        using var transfer = fixture.Sign();
        transfer.Headers.TryAddWithoutValidation(HeaderNames.TransferEncoding, PeerDiscoveryFixture.Chunked);
        foreach (var request in new[] { post, relative, route, query, fragment, userInfo, content, host, transfer })
        { await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.Sign(request)).Code).IsEqualTo(ErrorCode.Validation); }
    }

    /// <summary>Socket handler ownership remains independent and automatic redirect cannot cross signed recipient scope.</summary>
    [Test]
    public async Task RealHandlerUsesBoundedSocketsAndDisablesRedirects()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var handler = fixture.Sender.CreateHandler();
        var signed = (DelegatingHandler)handler;
        var sockets = (SocketsHttpHandler)signed.InnerHandler!;
        await Assert.That(sockets.AllowAutoRedirect).IsFalse();
        await Assert.That(sockets.ConnectTimeout).IsEqualTo(new PeerDiscoveryOptions().ConnectTimeout);
    }

    /// <summary>Nondefault socket settings reach the native handler and later options mutation cannot change the receiver snapshot.</summary>
    [Test]
    public async Task ConfiguredHandlerUsesFrozenNativeTransportDurations()
    {
        using var fixture = new PeerDiscoveryFixture();
        var configuredTimeout = TimeSpan.FromMilliseconds(175);
        var configuredLifetime = TimeSpan.FromSeconds(45);
        var settings = new PeerDiscoveryOptions
        {
            ConnectTimeout = configuredTimeout,
            PooledConnectionLifetime = configuredLifetime
        };
        using var security = new PeerSecurity(fixture.Secret, TimeProvider.System,
            RecoveryExecutionOptions.PeerDiscovery(settings));
        settings.ConnectTimeout = TimeSpan.Zero;
        settings.PooledConnectionLifetime = TimeSpan.Zero;
        using var handler = security.CreateHandler();
        var sockets = (SocketsHttpHandler)((DelegatingHandler)handler).InnerHandler!;

        await Assert.That(sockets.ConnectTimeout).IsEqualTo(configuredTimeout);
        await Assert.That(sockets.PooledConnectionLifetime).IsEqualTo(configuredLifetime);
        await Assert.That(sockets.AllowAutoRedirect).IsFalse();
    }

    /// <summary>Invalid credentials and replay limits fail before a discovery handler can be created.</summary>
    [Test]
    public async Task InvalidConstructorConfigurationIsTyped()
    {
        using var fixture = new PeerDiscoveryFixture();
        foreach (var length in new[] { 0, PeerDiscoveryProtocol.SecretBytes - 1, PeerDiscoveryProtocol.SecretBytes + 1 })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                using var invalid = new PeerSecurity(new byte[length], TimeProvider.System, RecoveryExecutionOptions.PeerDiscovery());
                using var invalidHandler = invalid.CreateHandler();
            }).Code).IsEqualTo(ErrorCode.Validation);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var invalid = new PeerSecurity(fixture.Secret, TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new PeerDiscoveryOptions { ReplayCapacity = 0 }));
            using var invalidHandler = invalid.CreateHandler();
        }).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var invalid = new PeerSecurity(fixture.Secret, TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new PeerDiscoveryOptions { ConnectTimeout = TimeSpan.Zero }));
            using var invalidHandler = invalid.CreateHandler();
        }).Code).IsEqualTo(ErrorCode.Validation);
    }
}
