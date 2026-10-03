using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireReplyShapeTests
{
    [Test]
    public async Task AcCache014PrepareReplyStatusesRequireProofExactlyForReady()
    {
        var statuses = new[]
        {
            CacheControlStatus.Ready, CacheControlStatus.Busy, CacheControlStatus.NotReady,
            CacheControlStatus.PolicyMismatch, CacheControlStatus.Replay, CacheControlStatus.Rejected,
            CacheControlStatus.Closed, CacheControlStatus.Capacity
        };
        var header = CacheControlWireTestData.Header(CacheControlOperation.Prepare, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var correlation = Correlation(header);

        foreach (var status in statuses)
        {
            var proof = status is CacheControlStatus.Ready
                ? CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot1)
                : null;
            await Assert.That(Encode(new CachePrepareReply(correlation, status, proof,
                CacheControlWireTestData.NondefaultMac))).IsTrue();
        }

        await Assert.That(Encode(new CachePrepareReply(correlation, CacheControlStatus.Ready, null,
            CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CachePrepareReply(correlation, CacheControlStatus.Busy,
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot1),
            CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CachePrepareReply(correlation, CacheControlStatus.AcceptedActive, null,
            CacheControlWireTestData.NondefaultMac))).IsFalse();
    }

    [Test]
    public async Task AcCache014GrantAcceptedRepliesNeedExactBindingAndPositiveSequenceShape()
    {
        var request = CacheControlWireTestData.GrantRequest();
        var correlation = Correlation(request.Header);
        var acceptedStatuses = new[] { CacheControlStatus.AcceptedActive, CacheControlStatus.AcceptedCold };
        var rejectedStatuses = new[]
        {
            CacheControlStatus.Busy, CacheControlStatus.NotReady, CacheControlStatus.PolicyMismatch,
            CacheControlStatus.StaleChallenge, CacheControlStatus.Replay, CacheControlStatus.Rejected,
            CacheControlStatus.Closed, CacheControlStatus.Capacity
        };

        foreach (var status in acceptedStatuses)
        {
            await Assert.That(Encode(new CacheGrantReply(correlation, status, request.GrantId,
                request.TargetBinding, request.Slot2Proof.ChallengeSequence, CacheControlWireTestData.NondefaultMac))).IsTrue();
        }
        foreach (var status in rejectedStatuses)
        {
            await Assert.That(Encode(new CacheGrantReply(correlation, status, request.GrantId,
                null, 0, CacheControlWireTestData.NondefaultMac))).IsTrue();
        }

        await Assert.That(Encode(new CacheGrantReply(correlation, CacheControlStatus.AcceptedActive,
            request.GrantId, null, 12, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheGrantReply(correlation, CacheControlStatus.AcceptedCold,
            request.GrantId, request.TargetBinding, 0, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheGrantReply(correlation, CacheControlStatus.Busy,
            request.GrantId, request.TargetBinding, 12, CacheControlWireTestData.NondefaultMac))).IsFalse();
    }

    [Test]
    public async Task AcCache014RevokeRepliesKeepOperationSpecificStatusBodies()
    {
        var revokeHeader = CacheControlWireTestData.Header(CacheControlOperation.Revoke,
            CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var revokeCorrelation = Correlation(revokeHeader);
        var revokeStatuses = new[]
        {
            CacheControlStatus.StaleChallenge, CacheControlStatus.PolicyMismatch, CacheControlStatus.Replay,
            CacheControlStatus.Rejected, CacheControlStatus.Closed, CacheControlStatus.Capacity
        };
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Revoked,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.Both, CacheControlWireTestData.NondefaultMac))).IsTrue();
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Revoked,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.PendingRemoved, CacheControlWireTestData.NondefaultMac))).IsTrue();
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Revoked,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.LeaseWithdrawn, CacheControlWireTestData.NondefaultMac))).IsTrue();
        foreach (var status in revokeStatuses)
        {
            await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, status,
                CacheControlWireTestData.GrantId, CacheRevokeEffect.None, CacheControlWireTestData.NondefaultMac))).IsTrue();
        }
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Revoked,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.None, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Busy,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.Both, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Busy,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.PendingRemoved, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheRevokeReply(revokeCorrelation, CacheControlStatus.PolicyMismatch,
            CacheControlWireTestData.GrantId, CacheRevokeEffect.LeaseWithdrawn, CacheControlWireTestData.NondefaultMac))).IsFalse();
    }

    [Test]
    public async Task AcCache014RefreshRepliesKeepOperationSpecificStatusBodies()
    {
        var refreshCorrelation = Correlation(CacheControlWireTestData.RefreshHeader());
        var refreshStatuses = new[]
        {
            CacheControlStatus.HintAcknowledged, CacheControlStatus.Busy, CacheControlStatus.NotReady,
            CacheControlStatus.PolicyMismatch, CacheControlStatus.Replay, CacheControlStatus.Rejected,
            CacheControlStatus.Closed, CacheControlStatus.Capacity
        };
        foreach (var status in refreshStatuses)
        {
            var session = status is CacheControlStatus.HintAcknowledged ? CacheControlWireTestData.SessionId : Guid.Empty;
            var round = status is CacheControlStatus.HintAcknowledged ? CacheControlWireTestData.RoundNonce : Guid.Empty;
            var address = status is CacheControlStatus.HintAcknowledged
                ? CacheControlWireTestData.Address(CacheVoterSlot.Slot0)
                : null;
            await Assert.That(Encode(new CacheRefreshReceipt(refreshCorrelation, status, session, round,
                address, CacheControlWireTestData.NondefaultMac))).IsTrue();
        }
        await Assert.That(Encode(new CacheRefreshReceipt(refreshCorrelation, CacheControlStatus.HintAcknowledged,
            Guid.Empty, Guid.Empty, null, CacheControlWireTestData.NondefaultMac))).IsFalse();
        await Assert.That(Encode(new CacheRefreshReceipt(refreshCorrelation, CacheControlStatus.HintAcknowledged,
            CacheControlWireTestData.SessionId, Guid.Empty, CacheControlWireTestData.Address(CacheVoterSlot.Slot0),
            CacheControlWireTestData.NondefaultMac))).IsTrue();
    }

    [Test]
    public async Task AcCache014NonAcknowledgedRefreshStatusesRejectEveryCoordinatorField()
    {
        var correlation = Correlation(CacheControlWireTestData.RefreshHeader());
        var statuses = new[]
        {
            CacheControlStatus.Busy, CacheControlStatus.NotReady, CacheControlStatus.PolicyMismatch,
            CacheControlStatus.Replay, CacheControlStatus.Rejected, CacheControlStatus.Closed,
            CacheControlStatus.Capacity
        };

        foreach (var status in statuses)
        {
            var empty = new CacheRefreshReceipt(correlation, status, Guid.Empty, Guid.Empty, null,
                CacheControlWireTestData.NondefaultMac);
            await Assert.That(Encode(empty)).IsTrue();
            await Assert.That(Encode(empty with { ActualCoordinatorSessionId = CacheControlWireTestData.SessionId })).IsFalse();
            await Assert.That(Encode(empty with { CurrentRoundNonce = CacheControlWireTestData.RoundNonce })).IsFalse();
            await Assert.That(Encode(empty with
            {
                ActualCoordinatorSiloAddress = CacheControlWireTestData.Address(CacheVoterSlot.Slot0)
            })).IsFalse();
        }
    }

    [Test]
    public async Task AcCache014EveryNonRevokedStatusRejectsEachRemovalEffect()
    {
        var correlation = Correlation(CacheControlWireTestData.Header(CacheControlOperation.Revoke,
            CacheVoterSlot.Slot0, CacheVoterSlot.Slot0));
        var statuses = new[]
        {
            CacheControlStatus.StaleChallenge, CacheControlStatus.PolicyMismatch, CacheControlStatus.Replay,
            CacheControlStatus.Rejected, CacheControlStatus.Closed, CacheControlStatus.Capacity
        };
        var effects = new[]
        {
            CacheRevokeEffect.PendingRemoved, CacheRevokeEffect.LeaseWithdrawn, CacheRevokeEffect.Both
        };

        foreach (var status in statuses)
        {
            foreach (var effect in effects)
            {
                var reply = new CacheRevokeReply(correlation, status, CacheControlWireTestData.GrantId,
                    effect, CacheControlWireTestData.NondefaultMac);
                await Assert.That(Encode(reply)).IsFalse();
            }
        }
    }

    [Test]
    public async Task AcCache014UnknownStatusAndRevokeEffectBytesRejectWithoutOutput()
    {
        var header = CacheControlWireTestData.Header(CacheControlOperation.Revoke,
            CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var correlation = Correlation(header);
        ICacheControlMessage[] invalid =
        [
            new CachePrepareReply(Correlation(CacheControlWireTestData.Header(CacheControlOperation.Prepare,
                CacheVoterSlot.Slot0, CacheVoterSlot.Slot1)), (CacheControlStatus)14, null,
                CacheControlWireTestData.NondefaultMac),
            new CacheRevokeReply(correlation, CacheControlStatus.Revoked, CacheControlWireTestData.GrantId,
                (CacheRevokeEffect)4, CacheControlWireTestData.NondefaultMac)
        ];

        foreach (var message in invalid)
        {
            await Assert.That(Encode(message)).IsFalse();
        }
    }

    private static bool Encode(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        if (!encoded && bytes.Length != 0)
        {
            throw new InvalidOperationException(PartialBytesMessage);
        }

        return encoded;
    }

    private static CacheReplyCorrelation Correlation(CacheControlHeader header)
        => new(header, CacheControlWireTestData.Digest(0x61));

    private const string PartialBytesMessage = "Invalid reply shapes must not expose partial transcript bytes.";
}
