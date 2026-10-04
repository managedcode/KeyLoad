using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireAuthenticatorTests
{
    [Test]
    public async Task AcCache014EveryRootMessageSignsAndAuthenticatesWithItsOwnDomain()
    {
        using var authenticator = CreateAuthenticator();
        await VerifyPrepareRoundTripAsync(authenticator);
        await VerifyGrantRoundTripAsync(authenticator);
        await VerifyRevokeRoundTripAsync(authenticator);
        await VerifyRefreshRoundTripAsync(authenticator);
    }

    [Test]
    public async Task AcCache014SignatureRejectsWrongDomainOperationPeerKeyAndTrustedScope()
    {
        using var authenticator = CreateAuthenticator();
        var signedPrepare = SignPrepare(authenticator, CacheControlWireTestData.PrepareRequest());
        var wrongOperation = signedPrepare with
        {
            Header = signedPrepare.Header with { Operation = CacheControlOperation.Grant }
        };
        var signedProof = SignProof(authenticator, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var proofMacAsReplyMac = new CachePrepareReply(Correlation(signedPrepare), CacheControlStatus.Busy,
            null, signedProof.Mac);
        var wrongClaimedScope = CacheControlWireTestData.PrepareRequest() with
        {
            Header = CacheControlWireTestData.PrepareRequest().Header with
            {
                ScopeHash = CacheControlWireTestData.Digest(0x9A)
            }
        };
        var claimedScopeShapeValid = CacheControlWire.TryEncodeForSigning(wrongClaimedScope, out var wrongScopeTranscript);
        var wrongScopeSigned = authenticator.TrySign(wrongClaimedScope, out var wrongScopeOutput);
        using var wrongKey = new CacheControlAuthenticator(PeerKey(0x91), CacheControlWireTestData.ScopeHash);
        using var wrongScope = new CacheControlAuthenticator(PeerKey(0x21), CacheControlWireTestData.Digest(0x99));

        await Assert.That(authenticator.TryAuthenticate(signedPrepare)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(wrongOperation)).IsFalse();
        await Assert.That(authenticator.TryAuthenticate(proofMacAsReplyMac)).IsFalse();
        await Assert.That(wrongKey.TryAuthenticate(signedPrepare)).IsFalse();
        await Assert.That(wrongScope.TryAuthenticate(signedPrepare)).IsFalse();
        await Assert.That(wrongScopeSigned).IsFalse();
        await Assert.That(wrongScopeOutput).IsNull();
        await Assert.That(claimedScopeShapeValid).IsTrue();
        await Assert.That(wrongScopeTranscript.Length > 0).IsTrue();
    }

    [Test]
    public async Task AcCache014ConstructorRejectsInvalidPeerKeyLengthAndDefaultScope()
    {
        var empty = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using var unexpected = new CacheControlAuthenticator([], CacheControlWireTestData.ScopeHash);
        });
        var shortKey = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using var unexpected = new CacheControlAuthenticator(PeerKey(0x21).AsSpan(0, 31), CacheControlWireTestData.ScopeHash);
        });
        var longKey = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using var unexpected = new CacheControlAuthenticator(PeerKey(0x21).Append((byte)0x41).ToArray(), CacheControlWireTestData.ScopeHash);
        });
        var defaultScope = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using var unexpected = new CacheControlAuthenticator(PeerKey(0x21), default);
        });

        await Assert.That(empty.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(shortKey.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(longKey.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(defaultScope.GetType()).IsEqualTo(typeof(ArgumentException));
    }

    [Test]
    public async Task AcCache014SignedPolicyMismatchAuthenticatesButNeverBecomesAcceptance()
    {
        using var authenticator = CreateAuthenticator();
        var requested = CacheControlWireTestData.PrepareRequest() with
        {
            Header = CacheControlWireTestData.PrepareRequest().Header with
            {
                PolicyHash = CacheControlWireTestData.Digest(0xA1)
            }
        };
        var signedRequest = SignPrepare(authenticator, requested);
        var signedMismatch = SignPrepareReply(authenticator, signedRequest, null, CacheControlStatus.PolicyMismatch);

        await Assert.That(authenticator.TryAuthenticate(signedRequest)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(signedMismatch)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(signedRequest, signedMismatch)).IsTrue();
        await Assert.That(signedMismatch.Status).IsEqualTo(CacheControlStatus.PolicyMismatch);
        await Assert.That(signedMismatch.Proof).IsNull();
        await Assert.That(signedMismatch.Status is CacheControlStatus.Ready).IsFalse();
    }

    [Test]
    public async Task AcCache014AuthenticatorCopiesCallerKeyAndClosesIdempotently()
    {
        var callerKey = PeerKey(0x21);
        using var owner = new CacheControlAuthenticator(callerKey, CacheControlWireTestData.ScopeHash);
        using var comparison = new CacheControlAuthenticator(PeerKey(0x21), CacheControlWireTestData.ScopeHash);
        var proof = CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var expected = SignProof(comparison, CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        Array.Fill(callerKey, byte.MaxValue);
        var actual = SignProof(owner, CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);

        await Assert.That(actual.Mac.FixedTimeEquals(expected.Mac)).IsTrue();
        await Assert.That(owner.TryAuthenticate(actual)).IsTrue();
        await Assert.That(actual with { Mac = proof.Mac }).IsEqualTo(proof);

        owner.Dispose();
        owner.Dispose();
        var disposed = Assert.ThrowsExactly<ObjectDisposedException>(() => owner.TryAuthenticate(actual));
        await Assert.That(disposed.GetType()).IsEqualTo(typeof(ObjectDisposedException));
    }

    private static async Task VerifyPrepareRoundTripAsync(CacheControlAuthenticator authenticator)
    {
        var proof = SignProof(authenticator, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var request = SignPrepare(authenticator, CacheControlWireTestData.PrepareRequest());
        var reply = SignPrepareReply(authenticator, request, proof);
        await Assert.That(authenticator.TryAuthenticate(proof)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(request)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(reply)).IsTrue();
        await Assert.That(reply.Proof).IsEqualTo(proof);
    }

    private static async Task VerifyGrantRoundTripAsync(CacheControlAuthenticator authenticator)
    {
        var request = SignGrant(authenticator, CacheControlWireTestData.GrantRequest());
        var reply = SignGrantReply(authenticator, request);
        await Assert.That(authenticator.TryAuthenticate(request)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(reply)).IsTrue();
        await Assert.That(reply.AcceptedBinding).IsEqualTo(request.TargetBinding);
    }

    private static async Task VerifyRevokeRoundTripAsync(CacheControlAuthenticator authenticator)
    {
        var request = SignRevoke(authenticator, CacheControlWireTestData.RevokeRequest());
        var reply = SignRevokeReply(authenticator, request);
        await Assert.That(authenticator.TryAuthenticate(request)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(reply)).IsTrue();
        await Assert.That(reply.Effect).IsEqualTo(CacheRevokeEffect.Both);
    }

    private static async Task VerifyRefreshRoundTripAsync(CacheControlAuthenticator authenticator)
    {
        var request = SignRefresh(authenticator, CacheControlWireTestData.RefreshHint());
        var reply = SignRefreshReply(authenticator, request);
        await Assert.That(authenticator.TryAuthenticate(request)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(reply)).IsTrue();
        await Assert.That(reply.ActualCoordinatorSessionId).IsEqualTo(CacheControlWireTestData.SessionId);
    }

}
