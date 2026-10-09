using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementObservedFailureColdRf3Assertions
{
    internal static async Task RequireRetainedAsync(PartitionMovementPublicParentRf3NativeCut[] refused,
        PartitionMovementPublicParentRf3NativeCut[] current)
    {
        foreach (var original in refused)
        {
            var actual = current.Single(cut => cut.Node == original.Node);
            if (original.Header is not null)
            {
                var phase = original.OriginalPhase!;
                await Assert.That(ZoneTreeStore.IsEncodedFrameLimitRejection(phase.OriginalResult)).IsTrue();
                await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
                await Assert.That(phase.OriginalGrant!.Settlement).IsNull();
                await Assert.That(phase.OriginalGrant.AbortDisposition).IsNull();
                await SqlRf3Protocol.EqualAsync(phase, actual.OriginalPhase);
            }
            else
            {
                await Assert.That(ZoneTreeStore.IsEncodedFrameLimitRejection(original.OriginalNativeResult)).IsTrue();
                await SqlRf3Protocol.EqualAsync(original.OriginalNativeResult, actual.OriginalNativeResult);
                await SqlRf3Protocol.EqualAsync(original.OriginalReceiverIssuance, actual.OriginalReceiverIssuance);
            }
        }
    }

    internal static async Task RequireDisposedAsync(PartitionMovementPublicParentRf3NativeCut[] refused,
        PartitionMovementPublicParentRf3NativeCut[] current, PartitionMoveResult aborted)
    {
        foreach (var original in refused.Where(cut => cut.Header is not null))
        {
            var actual = current.Single(cut => cut.Node == original.Node);
            var grant = actual.OriginalControlGrant
                ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await Assert.That(actual.Header!.TerminalObservationReceipt).IsNotNull();
            await SqlRf3Protocol.EqualAsync(aborted, actual.Header.TerminalResult);
            await Assert.That(actual.Header.PendingOriginalPhaseCommandId).IsNull();
            await Assert.That(actual.Header.InterruptedOriginalPhaseCommandId).IsNull();
            await Assert.That(grant.Settlement).IsNull();
            await Assert.That(grant.AbortDisposition).IsNotNull();
            await SqlRf3Protocol.EqualAsync(original.OriginalControlGrant, grant with { AbortDisposition = null });
            await Assert.That(actual.OutstandingMoveGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(actual.OutstandingPrincipalGrants).IsEqualTo((long?)PartitionMoveProtocol.EmptyCount);
            await Assert.That(actual.OutstandingDatabaseGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
        }
    }
}
