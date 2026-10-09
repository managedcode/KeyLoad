using KeyLoad.CrashHost.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextIncrementalCheckpointAssertions
{
    internal static async Task RequireAsync(string root, TextIndexMaintenanceRequest request,
        NativeTextIncrementalCrashResult actual, CancellationToken token)
    {
        var command = await NativeTextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(root,
            NativeTextIncrementalCheckpointEvidence.CommandFile(request.CommandId), token);
        var receipt = await NativeTextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(root,
            NativeTextIncrementalCheckpointEvidence.ReceiptFile(request.CommandId), token);
        await Assert.That(actual.Checkpoint).IsNotNull();
        var checkpoint = actual.Checkpoint ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        await Assert.That(NativeSerialization.Serialize(checkpoint).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(command.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(command.Effects).IsEmpty();
        await Assert.That(checkpoint.Receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(checkpoint.Receipt.Mutations).IsEmpty();
        await Assert.That(checkpoint.AlreadyProcessed).IsFalse();
        await Assert.That(checkpoint.Checkpoint).IsEqualTo(actual.Complete.Checkpoint);
        await Assert.That(checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(checkpoint.Receipt.Token.Incarnation).IsEqualTo(actual.OriginalReceipt.Token.Incarnation);
        await Assert.That(checkpoint.Receipt.Token.OwnershipEpoch).IsEqualTo(actual.OriginalReceipt.Token.OwnershipEpoch);
        await Assert.That(checkpoint.Receipt.Token.Position).IsGreaterThan(actual.OriginalReceipt.Token.Position);
        await Assert.That(checkpoint.Receipt.Token.Position).IsLessThanOrEqualTo(actual.AppliedPosition);
    }
}
