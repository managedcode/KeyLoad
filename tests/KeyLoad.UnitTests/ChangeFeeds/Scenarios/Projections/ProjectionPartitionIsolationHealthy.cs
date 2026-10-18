using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionPartitionIsolationHealthy
{
    internal static async Task<ProjectionPartitionIsolationState> RunAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionPartitionIsolationState original, CancellationToken token)
    {
        var source = original.Second;
        var outcomes = original.Outcomes.ToList();
        var id = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionConsumerIdentityFixture.Source,
            ProjectionPartitionIsolationFixture.HealthySource, ProjectionPartitionIsolationFixture.HealthyJson, 0L);
        var operation = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.Batch, id,
            new CommandRequest(id, source.Partition, [mutation]), source.Time);
        token.ThrowIfCancellationRequested();
        var produced = FeedNativeHealthyAdmission.Submit(owner, operation, explicitTime: true, token);
        var receipt = produced.Get<CommitReceipt>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(operation, receipt,
            new MutationReceipt(ProjectionPartitionIsolationFixture.PutKind, mutation.Collection, mutation.Id, 1L));
        outcomes.Add(new(operation, produced));
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        var entry = new OutboxEntry(4L, 0, receipt.Token, source.Time, mutation,
            new(ProjectionPartitionIsolationFixture.PutKind, mutation.Collection, mutation.Id, 1L), null,
            new(new(source.Partition, mutation.Collection, mutation.Id), 1L, mutation.Json, new RowAccess(), source.Time));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(source.Consumer, source.Definition, 3L, false), [entry], 4L, batch.Token, false));
        var effectId = Guid.NewGuid();
        var effect = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.CommitProjectionBatch, effectId,
            new CommitProjectionBatchRequest(effectId, source.Consumer, batch.Token,
                [new PutDocument(ProjectionConsumerIdentityFixture.Target, ProjectionPartitionIsolationFixture.HealthyEffect,
                    ProjectionPartitionIsolationFixture.HealthyJson, 0L)]));
        var completed = FeedNativeHealthyAdmission.Submit(owner, effect, explicitTime: false, token);
        var result = completed.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(effect, result.Receipt,
            new MutationReceipt(ProjectionPartitionIsolationFixture.PutKind, ProjectionConsumerIdentityFixture.Target,
                ProjectionPartitionIsolationFixture.HealthyEffect, 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, 4L));
        outcomes.Add(new(effect, completed));
        await ProjectionPartitionIsolationSettlement.FilterAsync(owner, source.Consumer, source.Definition, 4L, 5L, outcomes, token);
        var continued = original with
        {
            Outcomes = [.. outcomes],
            HealthySourceReceipt = receipt,
            HealthyEffectReceipt = result.Receipt
        };
        await ProjectionPartitionIsolationAssertions.VerifyAsync(owner, store, continued, token);
        return continued with
        {
            Image = ProjectionConsumerIdentityAssertions.Image(store),
            Position = store.Position
        };
    }
}
