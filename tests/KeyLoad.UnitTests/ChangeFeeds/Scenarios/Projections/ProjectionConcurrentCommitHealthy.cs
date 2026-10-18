using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionConcurrentCommitHealthy
{
    internal static async Task RunAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConcurrentCommitState state, CancellationToken token)
    {
        var source = state.Source;
        var id = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionConsumerIdentityFixture.Source, "c", SourceJson, 0L);
        var command = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.Batch, id,
            new CommandRequest(id, source.Partition, [mutation]), source.Time);
        token.ThrowIfCancellationRequested();
        var produced = FeedNativeHealthyAdmission.Submit(owner, command, explicitTime: true, token);
        var receipt = produced.Get<CommitReceipt>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(command, receipt,
            new MutationReceipt(PutKind, ProjectionConsumerIdentityFixture.Source, "c", 1L));
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        var entry = new OutboxEntry(4L, 0, receipt.Token, source.Time, mutation,
            new MutationReceipt(PutKind, ProjectionConsumerIdentityFixture.Source, "c", 1L), null,
            new DocumentRecord(source.Creation.ExpectedReference(new(source.Partition, ProjectionConsumerIdentityFixture.Source, "c")), 1L,
                SourceJson, new RowAccess(), source.Time));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(source.Consumer, source.Definition, 3L, false), [entry], 4L, batch.Token, false));
        var effectId = Guid.NewGuid();
        var effect = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.CommitProjectionBatch, effectId,
            new CommitProjectionBatchRequest(effectId, source.Consumer, batch.Token,
                [new PutDocument(ProjectionConsumerIdentityFixture.Target, "healthy", EffectJson, 0L)]));
        var completed = FeedNativeHealthyAdmission.Submit(owner, effect, explicitTime: false, token);
        var result = completed.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(effect, result.Receipt,
            new MutationReceipt(PutKind, ProjectionConsumerIdentityFixture.Target, "healthy", 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, 4L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(source.Partition, ProjectionConsumerIdentityFixture.Target, "healthy"), result.Receipt.Token, token),
            new DocumentResult(source.Creation.ExpectedReference(new(source.Partition, ProjectionConsumerIdentityFixture.Target, "healthy")), 1L, EffectJson, false, []));
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(source.Partition, ProjectionConsumerIdentityFixture.Source, "c"), receipt.Token, token),
            new DocumentResult(source.Creation.ExpectedReference(new(source.Partition, ProjectionConsumerIdentityFixture.Source, "c")), 1L, SourceJson, false, []));
        await AdvanceAsync(owner, store, source, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, command, produced, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, effect, completed, token);
        await ProjectionConcurrentCommitFixture.ReplayAsync(owner, store, source, state.First, state.FirstResult,
            state.Second, state.SecondResult, state.Filtered, state.Advanced, token);
    }

    private static async Task AdvanceAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerIdentityState source, CancellationToken token)
    {
        var tail = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(tail,
            new ProjectionBatch(new(source.Consumer, source.Definition, 4L, false), [], 5L, tail.Token, false));
        var id = Guid.NewGuid();
        var operation = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, source.Consumer, tail.Token, []));
        var outcome = owner.ApplyEmbedded(operation, token);
        var result = outcome.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(operation, result.Receipt);
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, 5L));
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, operation, outcome, token);
    }

    private const string PutKind = "putDocument";
    private const string SourceJson = "{\"n\":3}";
    private const string EffectJson = "{\"sourceCount\":3}";
}
