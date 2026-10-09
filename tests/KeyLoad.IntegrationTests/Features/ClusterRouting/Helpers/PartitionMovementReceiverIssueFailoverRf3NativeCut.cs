using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Cold cuts keep unknown effects charged and separate business bytes from real proof checkpoint commits.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3NativeCut
{
    internal static Guid StageId(PartitionMovementPublicParentRf3Seed seed)
        => PartitionMovementParentPhaseIds.For(seed.FirstRequest, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            PartitionMovementParentPhaseRole.StagePage);

    internal static PartitionMoveParentPhase Pending(PartitionMovementPublicParentRf3NativeCut[] cuts)
        => cuts.First(cut => cut.Pending is not null).Pending!;

    internal static async Task RequireUnknownAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveParentPhase original, bool observed)
    {
        await Assert.That(original.Stage).IsEqualTo(PartitionMovePeerStage.StagePage);
        await Assert.That(original.OriginalResult).IsNull();
        await Assert.That(original.ObservationCheckpointReceipt).IsNull();
        await Assert.That(original.OriginalReceiverIssuePacket).IsNotNull();
        await Assert.That(original.ReceiverIssuePacketCheckpointReceipt).IsNotNull();
        await Assert.That(original.OriginalReceiverIssuanceWitness is not null).IsEqualTo(observed);
        foreach (var control in cuts.Where(cut => cut.Header is not null))
        {
            await Assert.That(control.Header!.TerminalResult).IsNull();
            await Assert.That(control.Header.PendingOriginalPhaseCommandId).IsEqualTo((Guid?)original.OriginalPhaseCommandId);
            await Assert.That(control.OutstandingMoveGrants).IsGreaterThan((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(control.OutstandingPrincipalGrants > PartitionMoveProtocol.EmptyCount).IsTrue();
            await Assert.That(control.OutstandingDatabaseGrants).IsGreaterThan((long)PartitionMoveProtocol.EmptyCount);
            var rows = PartitionMovementCapturePointerRf3Fault.Rows(control);
            await Assert.That(rows.ContainsKey(Convert.ToHexString(PartitionMoveParentKeys.Active(original.OriginalPhase!.Partition)))).IsTrue();
            await SqlRf3Protocol.EqualAsync(original.OriginalGrant, control.OriginalControlGrant);
        }
        await Assert.That(cuts.All(cut => cut.OriginalNativeResult is null)).IsTrue();
    }

    internal static async Task RequireObservationOnlyAsync(PartitionMoveParentPhase original,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        var actual = Pending(after);
        await Assert.That(actual.OriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(actual.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
        await Assert.That(actual.OriginalRequestNonce).IsEqualTo(original.OriginalRequestNonce);
        await Assert.That(actual.OriginalPhaseIdentityDigest).IsEqualTo(original.OriginalPhaseIdentityDigest);
        await SqlRf3Protocol.EqualAsync(original.OriginalPhase, actual.OriginalPhase);
        await SqlRf3Protocol.EqualAsync(original.OriginalGrant, actual.OriginalGrant);
        await SqlRf3Protocol.EqualAsync(original.OriginalReceiverIssuePacket, actual.OriginalReceiverIssuePacket);
        await RequireUnknownAsync(after, actual, observed: true);
        await PartitionMovementReceiverIssueFailoverRf3ProofAssertions.RequireAsync(after, actual, null);
        foreach (var previous in before)
        {
            var current = after.Single(cut => cut.Node == previous.Node);
            foreach (var family in PartitionRecordFamilies.All.Where(family => family is not
                (PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocatorV2 or PartitionRecordFamilies.OutcomeLocator)))
            {
                var prefix = Convert.ToHexString(KeySpace.Partition(family, original.OriginalPhase!.Partition));
                await SqlRf3Protocol.EqualAsync(previous.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray(),
                    current.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
            }
            await SqlRf3Protocol.EqualAsync(previous.OriginalReceiverIssuance, current.OriginalReceiverIssuance);
            await SqlRf3Protocol.EqualAsync(previous.OriginalControlGrant, current.OriginalControlGrant);
        }
    }
    internal static async Task RequireAbortDispositionAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveParentPhase original, PartitionMoveResult aborted)
    {
        foreach (var control in cuts.Where(cut => cut.Header is not null))
        {
            await SqlRf3Protocol.EqualAsync(aborted, control.Header!.TerminalResult);
            await Assert.That(control.Header.TerminalObservationReceipt).IsNotNull();
            await Assert.That(control.Header.PendingOriginalPhaseCommandId).IsNull();
            await Assert.That(control.Header.InterruptedOriginalPhaseCommandId).IsNull();
            var retained = control.OriginalPhase!;
            await Assert.That(retained.OriginalResult).IsNull();
            await SqlRf3Protocol.EqualAsync(original.OriginalGrant, retained.OriginalGrant);
            await Assert.That(control.OriginalControlGrant!.Settlement).IsNull();
            await Assert.That(control.OriginalControlGrant.AbortDisposition).IsNotNull();
            await SqlRf3Protocol.EqualAsync(original.OriginalGrant,
                control.OriginalControlGrant with { AbortDisposition = null });
            await Assert.That(control.OutstandingMoveGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(control.OutstandingPrincipalGrants).IsEqualTo((long?)PartitionMoveProtocol.EmptyCount);
            await Assert.That(control.OutstandingDatabaseGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
        }
        await Assert.That(cuts.All(cut => cut.OriginalNativeResult is null)).IsTrue();
    }

}
