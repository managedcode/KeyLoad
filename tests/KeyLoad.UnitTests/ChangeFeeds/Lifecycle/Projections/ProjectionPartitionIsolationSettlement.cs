using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionPartitionIsolationSettlement
{
    internal static async Task RunAsync(DatabaseEngine owner, ProjectionConsumerIdentityState source,
        List<ProjectionPartitionOutcome> outcomes, CancellationToken token)
    {
        var completed = owner.ApplyEmbedded(source.Complete, token);
        var result = completed.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(source.Complete, result.Receipt,
            new MutationReceipt(ProjectionPartitionIsolationFixture.PutKind, ProjectionConsumerIdentityFixture.Target, "effect", 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, 2L));
        outcomes.Add(new(source.Complete, completed));
        await FilterAsync(owner, source.Consumer, source.Definition, 2L, 3L, outcomes, token);
        var purge = ProjectionPartitionIsolationFixture.Purge(source.Partition, 3L);
        var purged = owner.ApplyEmbedded(purge, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(purged.Get<OutboxHead>(), new OutboxHead(3L, 4L, 0L, 0L));
        outcomes.Add(new(purge, purged));
    }

    internal static async Task FilterAsync(DatabaseEngine owner, ProjectionConsumerRef consumer,
        ProjectionConsumerDefinition definition, long checkpoint, long through,
        List<ProjectionPartitionOutcome> outcomes, CancellationToken token)
    {
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(consumer, definition, checkpoint, false), [], through, batch.Token, false));
        var id = Guid.NewGuid();
        var operation = ProjectionConsumerIdentityFixture.Operation(OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, []));
        var advanced = owner.ApplyEmbedded(operation, token);
        var result = advanced.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(operation, result.Receipt);
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, through));
        outcomes.Add(new(operation, advanced));
    }
}
