using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterReceipts
{
    internal static async Task TokenAsync(CommitToken actual, PartitionRef partition)
    {
        await Assert.That(actual.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(actual.Position).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
    }

    internal static async Task PatchAsync(CommitReceipt actual, SafeProjectorOperationClusterState state,
        Guid id, long revision)
    {
        await TokenAsync(actual.Token, state.Partition);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(id, actual.Token,
            [new(SafeProjectorOperationClusterProtocol.PatchKind, state.Collection, SafeProjectorOperationClusterProtocol.First, revision)],
            DurabilityProfile.ProcessDurable, 3), actual);
    }
}
