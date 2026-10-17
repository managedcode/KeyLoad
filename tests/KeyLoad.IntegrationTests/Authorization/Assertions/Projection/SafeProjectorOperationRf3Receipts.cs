using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Receipts
{
    internal static async Task TokenAsync(CommitToken actual, PartitionRef partition, AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(placement.Partition).IsEqualTo(partition);
        await Assert.That(actual.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Position).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
    }

    internal static async Task PatchAsync(CommitReceipt actual, SafeProjectorOperationRf3State state,
        Guid id, long revision, AtomicPartitionPlacementResolution placement)
    {
        await TokenAsync(actual.Token, state.Partition, placement);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(id, actual.Token,
            [new(SafeProjectorOperationRf3Protocol.PatchKind, state.Collection, SafeProjectorOperationRf3Protocol.First, revision)],
            DurabilityProfile.QuorumProcessDurable), actual);
    }
}
