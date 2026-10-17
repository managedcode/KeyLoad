using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ClaimAssertions
{
    internal static async Task ApplyingAsync(PlacementIntentV1 pending, PlacementIntentV1 actual,
        PartitionMoveRequest move, Guid id, ApplyPlacementIntentRequest request,
        RequestCqrsProbeMarkerRecord marker, RequestCqrsMovementJournalRecord proof,
        ReplicaSiloDiscovery[] discovery)
    {
        await Assert.That(actual.Status).IsEqualTo(PlacementIntentStatus.Applying);
        await Assert.That(actual.Revision).IsEqualTo(checked(pending.Revision + TenantPlacementPolicyProtocol.RevisionStep));
        await Assert.That(actual.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(actual.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(actual.CompletedMoves).IsEqualTo(TenantPlacementPolicyProtocol.NoCompletedMoves);
        await Assert.That(actual.Receipts.Length).IsEqualTo(0);
        await Assert.That(actual.IntentId).IsEqualTo(pending.IntentId);
        await Assert.That(actual.PrincipalId).IsEqualTo(pending.PrincipalId);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
        await SqlRf3Protocol.EqualAsync(pending.Plan, actual.Plan);
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, actual.ControlOwner);
        await Assert.That(actual.Claim).IsNotNull();
        var claim = actual.Claim!;
        var owner = discovery.Single(value => value.VoterId == claim.VoterId);
        await Assert.That(owner.SiloAddress).IsEqualTo(claim.SiloAddress);
        await Assert.That(owner.Incarnation).IsEqualTo(pending.ControlOwner.Incarnation);
        await Assert.That(pending.ControlOwner.VoterIds.Contains(claim.VoterId, StringComparer.Ordinal)).IsTrue();
        await Assert.That(claim.Nonce).IsNotEqualTo(Guid.Empty);
        await Assert.That(claim.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(discovery.Select(value => value.SiloAddress)));
        await Assert.That(claim.CohortFingerprint).IsEqualTo(pending.Plan.CohortFingerprint);
        await Assert.That(claim.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await ProofAsync(pending, move, marker, proof, discovery);
    }

    private static async Task ProofAsync(PlacementIntentV1 pending, PartitionMoveRequest move,
        RequestCqrsProbeMarkerRecord marker, RequestCqrsMovementJournalRecord proof, ReplicaSiloDiscovery[] discovery)
    {
        await Assert.That(proof.Error).IsNull();
        await Assert.That(proof.CommandId).IsEqualTo(PartitionMovementParentPhaseIds.For(move,
            pending.PrincipalId, PartitionMovementParentPhaseRole.Prepare));
        await Assert.That(proof.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(proof.ArmId).IsEqualTo(marker.ArmId);
        await Assert.That(proof.MoveId).IsEqualTo(move.MoveId);
        await Assert.That(proof.Partition).IsEqualTo(move.Partition);
        await Assert.That(proof.PrincipalId).IsEqualTo(pending.PrincipalId);
        await Assert.That(proof.Stage).IsEqualTo(PartitionMovePeerStage.ControlPrepare);
        await Assert.That(proof.Journal).IsNotNull();
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, proof.Journal!.PhysicalOwner);
        await Assert.That(proof.Journal.AppliedPosition).IsGreaterThan(0);
        var receiver = discovery.Single(value => value.VoterId == proof.Voter);
        await Assert.That(receiver.SiloAddress).IsEqualTo(proof.SiloAddress);
        await Assert.That(receiver.Incarnation).IsEqualTo(pending.ControlOwner.Incarnation);
        await Assert.That(pending.ControlOwner.VoterIds.Contains(proof.Voter, StringComparer.Ordinal)).IsTrue();
    }
}
