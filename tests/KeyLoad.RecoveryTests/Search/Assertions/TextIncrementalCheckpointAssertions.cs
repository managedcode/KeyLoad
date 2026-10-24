namespace KeyLoad.RecoveryTests.Search;

internal static class TextIncrementalCheckpointAssertions
{
    internal static async Task RequireAsync(string root, TextIndexMaintenanceRequest request,
        TextIncrementalCrashResult actual, CancellationToken token)
    {
        var command = await TextIncrementalEvidenceFiles.ReadAsync<CommitProjectionBatchRequest>(root,
            TextIncrementalCheckpointEvidence.CommandFile(request.CommandId), token);
        var receipt = await TextIncrementalEvidenceFiles.ReadAsync<ProjectionBatchResult>(root,
            TextIncrementalCheckpointEvidence.ReceiptFile(request.CommandId), token);
        await Assert.That(actual.Checkpoint).IsNotNull();
        var checkpoint = actual.Checkpoint ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        await Assert.That(OrleansSerialization.Serialize(checkpoint).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(command.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(command.Effects).IsEmpty();
        await Assert.That(checkpoint.Receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(checkpoint.Receipt.Mutations).IsEmpty();
        await Assert.That(checkpoint.AlreadyProcessed).IsFalse();
        await Assert.That(checkpoint.Checkpoint).IsEqualTo(actual.Complete.Checkpoint);
        await Assert.That(checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(checkpoint.Receipt.Token.Incarnation).IsEqualTo(actual.OriginalReceipt.Token.Incarnation);

        await Assert.That(checkpoint.Receipt.Token.Position).IsGreaterThan(actual.OriginalReceipt.Token.Position);
        await Assert.That(checkpoint.Receipt.Token.Position).IsLessThanOrEqualTo(actual.AppliedPosition);
    }
}
