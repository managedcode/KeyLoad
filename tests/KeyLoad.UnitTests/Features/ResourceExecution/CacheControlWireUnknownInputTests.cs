using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireUnknownInputTests
{
    [Test]
    public async Task AcCache014UnknownMarkersRejectWithoutReadingCallerGetters()
    {
        using var authenticator = CreateAuthenticator();
        var signedNull = CacheControlWire.TryEncodeSigned(null, out var completeNullBytes);
        var signingNull = CacheControlWire.TryEncodeForSigning(null, out var signingNullBytes);
        var authenticatedNull = authenticator.TryAuthenticate(null);
        var unknownMessage = new UnknownMessage();
        var unknownRequest = new UnknownRequest();
        var unknownReply = new UnknownReply();
        var knownRequest = SignPrepare(authenticator, CacheControlWireTestData.PrepareRequest());
        var complete = CacheControlWire.TryEncodeSigned(unknownMessage, out var completeBytes);
        var signing = CacheControlWire.TryEncodeForSigning(unknownMessage, out var signingBytes);
        var requestComplete = CacheControlWire.TryEncodeSigned(unknownRequest, out var requestBytes);
        var requestSigning = CacheControlWire.TryEncodeForSigning(unknownRequest, out var requestSigningBytes);
        var replyComplete = CacheControlWire.TryEncodeSigned(unknownReply, out var replyBytes);
        var replySigning = CacheControlWire.TryEncodeForSigning(unknownReply, out var replySigningBytes);
        var created = CacheControlCorrelation.TryCreate(unknownRequest, out var correlation);
        var matched = CacheControlCorrelation.TryMatch(unknownRequest, unknownReply);
        var knownRequestMatched = CacheControlCorrelation.TryMatch(knownRequest, unknownReply);

        await Assert.That(signedNull).IsFalse();
        await Assert.That(completeNullBytes.Length).IsEqualTo(0);
        await Assert.That(signingNull).IsFalse();
        await Assert.That(signingNullBytes.Length).IsEqualTo(0);
        await Assert.That(authenticatedNull).IsFalse();
        await Assert.That(complete).IsFalse();
        await Assert.That(completeBytes.Length).IsEqualTo(0);
        await Assert.That(signing).IsFalse();
        await Assert.That(signingBytes.Length).IsEqualTo(0);
        await Assert.That(requestComplete).IsFalse();
        await Assert.That(requestBytes.Length).IsEqualTo(0);
        await Assert.That(requestSigning).IsFalse();
        await Assert.That(requestSigningBytes.Length).IsEqualTo(0);
        await Assert.That(replyComplete).IsFalse();
        await Assert.That(replyBytes.Length).IsEqualTo(0);
        await Assert.That(replySigning).IsFalse();
        await Assert.That(replySigningBytes.Length).IsEqualTo(0);
        await Assert.That(created).IsFalse();
        await Assert.That(correlation).IsNull();
        await Assert.That(matched).IsFalse();
        await Assert.That(knownRequestMatched).IsFalse();
        await Assert.That(authenticator.TryAuthenticate(unknownMessage)).IsFalse();
        await Assert.That(authenticator.TryAuthenticate(unknownRequest)).IsFalse();
        await Assert.That(authenticator.TryAuthenticate(unknownReply)).IsFalse();

        authenticator.Dispose();
        var nullAfterDispose = Assert.ThrowsExactly<ObjectDisposedException>(() => authenticator.TryAuthenticate(null));
        var unknownAfterDispose = Assert.ThrowsExactly<ObjectDisposedException>(() => authenticator.TryAuthenticate(new UnknownMessage()));
        await Assert.That(nullAfterDispose.GetType()).IsEqualTo(typeof(ObjectDisposedException));
        await Assert.That(unknownAfterDispose.GetType()).IsEqualTo(typeof(ObjectDisposedException));
    }

    private static T ThrowIfRead<T>() => throw new InvalidOperationException(UnknownGetterMessage);

    private sealed class UnknownMessage : ICacheControlMessage
    {
        public CacheControlDigest Mac => ThrowIfRead<CacheControlDigest>();
    }

    private sealed class UnknownRequest : ICacheControlRequest
    {
        public CacheControlDigest Mac => ThrowIfRead<CacheControlDigest>();
        public CacheControlHeader Header => ThrowIfRead<CacheControlHeader>();
    }

    private sealed class UnknownReply : ICacheControlReply
    {
        public CacheControlDigest Mac => ThrowIfRead<CacheControlDigest>();
        public CacheReplyCorrelation? Correlation => ThrowIfRead<CacheReplyCorrelation?>();
    }

    private const string UnknownGetterMessage = "A closed wire API read a property from an unknown marker.";
}
