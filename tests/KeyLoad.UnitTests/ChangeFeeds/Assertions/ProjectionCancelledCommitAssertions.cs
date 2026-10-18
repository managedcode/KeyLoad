using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionCancelledCommitAssertions
{
    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerIdentityState state, CancellationToken token)
    {
        var image = ProjectionConsumerIdentityAssertions.Image(store);
        var position = store.Position;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancellation.CancelAsync();
        Result<object>? partial = null;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            partial = owner.ApplyEmbedded(state.Complete, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(partial).IsNull();
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(owner, state);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state);
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.Partition, ProjectionConsumerIdentityFixture.Target, "effect"), cancellationToken: token)).IsNull();
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
    }

    internal static async Task HealthyAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerIdentityState state, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionConsumerIdentityFixture.Source, "c", "{\"n\":3}", 0L);
        var source = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.Batch, id,
            new CommandRequest(id, state.Partition, [mutation]));
        var original = FeedNativeHealthyAdmission.Submit(owner, source, explicitTime: false, token);
        var receipt = original.Get<CommitReceipt>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(source, receipt,
            new MutationReceipt("putDocument", ProjectionConsumerIdentityFixture.Source, "c", 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.Partition, ProjectionConsumerIdentityFixture.Source, "c"), receipt.Token, token),
            new DocumentResult(state.Creation.ExpectedReference(new(state.Partition, ProjectionConsumerIdentityFixture.Source, "c")), 1L, "{\"n\":3}", false, []));
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer));
        var entry = new OutboxEntry(4L, 0, receipt.Token, FeedAuthorityClock.Epoch, mutation,
            new("putDocument", ProjectionConsumerIdentityFixture.Source, "c", 1L), null,
            new(state.Creation.ExpectedReference(new(state.Partition, ProjectionConsumerIdentityFixture.Source, "c")), 1L, mutation.Json, new RowAccess(), FeedAuthorityClock.Epoch));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(state.Consumer, state.Definition, 3L, false), [entry], 4L, batch.Token, false));
        var advanceId = Guid.NewGuid();
        var advance = ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.CommitProjectionBatch, advanceId,
            new CommitProjectionBatchRequest(advanceId, state.Consumer, batch.Token, []));
        var advanced = owner.ApplyEmbedded(advance, token);
        var result = advanced.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(advance, result.Receipt);
        await Assert.That(result.AlreadyProcessed).IsFalse();
        await Assert.That(result.Checkpoint).IsEqualTo(4L);
        var empty = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(empty,
            new ProjectionBatch(new(state.Consumer, state.Definition, 4L, false), [], 4L, empty.Token, false));
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source, original, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, advance, advanced, token);
        await ProjectionConsumerIdentityAssertions.EffectAsync(owner, state);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state);
    }
}
