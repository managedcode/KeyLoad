using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireCorrelationTests
{
    [Test]
    public async Task AcCache014RequestDigestIncludesOuterAndEveryNestedProofMac()
    {
        var request = CacheControlWireTestData.GrantRequest();
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);
        var changedOuter = request with { Mac = CacheControlWireTestData.Digest(0x72) };
        var changedHeader = request with { Header = request.Header with { SentUnixMilliseconds = request.Header.SentUnixMilliseconds + 1 } };
        ICacheControlRequest[] changedProofs =
        [
            request with { Slot0Proof = request.Slot0Proof with { Mac = CacheControlWireTestData.Digest(0x73) } },
            request with { Slot1Proof = request.Slot1Proof with { Mac = CacheControlWireTestData.Digest(0x74) } },
            request with { Slot2Proof = request.Slot2Proof with { Mac = CacheControlWireTestData.Digest(0x75) } }
        ];

        await Assert.That(created).IsTrue();
        await Assert.That(correlation).IsNotNull();
        await Assert.That(TryDigest(changedOuter)).IsNotEqualTo(correlation!.SignedRequestDigest);
        foreach (var changedProof in changedProofs)
        {
            await Assert.That(TryDigest(changedProof)).IsNotEqualTo(correlation.SignedRequestDigest);
        }
        await Assert.That(TryDigest(changedHeader)).IsNotEqualTo(correlation.SignedRequestDigest);
    }

    [Test]
    public async Task AcCache014RequestReplyHeaderAndDigestMustMatch()
    {
        var prepare = CacheControlWireTestData.PrepareRequest();
        var prepareCorrelation = Correlation(prepare);
        var readyProof = CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var prepareReply = new CachePrepareReply(prepareCorrelation, CacheControlStatus.Ready,
            readyProof, CacheControlWireTestData.NondefaultMac);
        var mismatchedPrepare = prepareReply with
        {
            Correlation = prepareCorrelation with
            {
                Header = prepareCorrelation.Header with { SentUnixMilliseconds = prepareCorrelation.Header.SentUnixMilliseconds + 1 }
            }
        };
        var mismatchedDigest = prepareReply with
        {
            Correlation = prepareCorrelation with { SignedRequestDigest = CacheControlWireTestData.Digest(0x76) }
        };

        await Assert.That(CacheControlCorrelation.TryMatch(prepare, prepareReply)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(prepare, mismatchedPrepare)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(prepare, mismatchedDigest)).IsFalse();
    }

    [Test]
    public async Task AcCache014AcceptedTargetBindingAndTargetSequenceMustMatch()
    {
        var grant = CacheControlWireTestData.GrantRequest();
        var grantCorrelation = Correlation(grant);
        var accepted = new CacheGrantReply(grantCorrelation, CacheControlStatus.AcceptedActive,
            CacheControlWireTestData.GrantId, grant.TargetBinding, grant.Slot2Proof.ChallengeSequence,
            CacheControlWireTestData.NondefaultMac);
        var wrongNodeIdBinding = accepted with
        {
            AcceptedBinding = grant.TargetBinding with
            {
                NodeId = Guid.Parse("98765432-10fe-dcba-9876-543210fedcba")
            }
        };
        var wrongAddressBinding = accepted with
        {
            AcceptedBinding = grant.TargetBinding with
            {
                SiloAddress = SiloAddress.New(IPAddress.Parse("127.0.0.9"), 11119, 8).ToParsableString()
            }
        };
        var wrongSequence = accepted with { AcceptedSequence = grant.Slot1Proof.ChallengeSequence };
        var negative = accepted with
        {
            Status = CacheControlStatus.PolicyMismatch,
            AcceptedBinding = null,
            AcceptedSequence = 0
        };

        await Assert.That(CacheControlCorrelation.TryMatch(grant, accepted)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(grant, wrongNodeIdBinding)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(grant, wrongAddressBinding)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(grant, wrongSequence)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(grant, negative)).IsTrue();
    }

    [Test]
    public async Task AcCache014AllOperationsRequireTheirExactCorrelatedReplyTypes()
    {
        var revoke = CacheControlWireTestData.RevokeRequest();
        var revokeReply = new CacheRevokeReply(Correlation(revoke), CacheControlStatus.Revoked,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.Both, CacheControlWireTestData.NondefaultMac);
        var refresh = CacheControlWireTestData.RefreshHint();
        var refreshCorrelation = Correlation(refresh);
        var refreshReply = new CacheRefreshReceipt(refreshCorrelation, CacheControlStatus.HintAcknowledged,
            CacheControlWireTestData.SessionId, CacheControlWireTestData.RoundNonce,
            CacheControlWireTestData.Address(CacheVoterSlot.Slot0), CacheControlWireTestData.NondefaultMac);
        var wrongOperationHeader = refreshReply with
        {
            Correlation = refreshCorrelation with
            {
                Header = refreshCorrelation.Header with { Operation = CacheControlOperation.Revoke }
            }
        };
        var defaultMacReply = revokeReply with { Mac = default };

        await Assert.That(CacheControlCorrelation.TryMatch(revoke, revokeReply)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(refresh, refreshReply)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(refresh, wrongOperationHeader)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(revoke, defaultMacReply)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(revoke, refreshReply)).IsFalse();
        await Assert.That(CacheControlCorrelation.TryMatch(refresh, revokeReply)).IsFalse();
    }

    [Test]
    public async Task AcCache014CorrelationCreationClearsOutputForMissingOrUnsignedRequests()
    {
        var missing = CacheControlCorrelation.TryCreate(null, out var missingCorrelation);
        var defaultDigest = CacheControlWireTestData.PrepareRequest() with { Mac = default };
        var defaultDigestCreated = CacheControlCorrelation.TryCreate(defaultDigest, out var defaultDigestCorrelation);
        var unsigned = CacheControlWireTestData.PrepareRequest() with { Mac = default };
        var unsignedCreated = CacheControlCorrelation.TryCreate(unsigned, out var unsignedCorrelation);

        await Assert.That(missing).IsFalse();
        await Assert.That(missingCorrelation).IsNull();
        await Assert.That(defaultDigestCreated).IsFalse();
        await Assert.That(defaultDigestCorrelation).IsNull();
        await Assert.That(unsignedCreated).IsFalse();
        await Assert.That(unsignedCorrelation).IsNull();
    }

    [Test]
    public async Task AcCache014CorrelationRejectsMissingReplyHeaderWithoutOutputMatch()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var reply = new CachePrepareReply(Correlation(request), CacheControlStatus.Busy, null,
            CacheControlWireTestData.NondefaultMac);
        var malformed = reply with
        {
            Correlation = reply.Correlation! with { Header = null! }
        };
        var defaultDigest = reply with
        {
            Correlation = reply.Correlation! with { SignedRequestDigest = default }
        };
        var encoded = CacheControlWire.TryEncodeSigned(malformed, out var bytes);
        var defaultDigestEncoded = CacheControlWire.TryEncodeSigned(defaultDigest, out var defaultDigestBytes);

        await Assert.That(encoded).IsFalse();
        await Assert.That(bytes.Length).IsEqualTo(0);
        await Assert.That(CacheControlCorrelation.TryMatch(request, malformed)).IsFalse();
        await Assert.That(defaultDigestEncoded).IsFalse();
        await Assert.That(defaultDigestBytes.Length).IsEqualTo(0);
        await Assert.That(CacheControlCorrelation.TryMatch(request, defaultDigest)).IsFalse();
    }

    private static CacheReplyCorrelation Correlation(ICacheControlRequest request)
    {
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);
        if (!created || correlation is null)
        {
            throw new InvalidOperationException(CorrelationFailureMessage);
        }

        return correlation;
    }

    private static CacheControlDigest TryDigest(ICacheControlRequest request)
    {
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);
        if (!created || correlation is null)
        {
            throw new InvalidOperationException(CorrelationFailureMessage);
        }

        return correlation.SignedRequestDigest;
    }

    private const string CorrelationFailureMessage = "The valid request did not produce its exact correlation digest.";
}
