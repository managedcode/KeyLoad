using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionCheckpointRetentionSettlement
{
    internal static async Task<ProjectionCheckpointRetentionContinuation> RunAsync(DatabaseEngine owner,
        ZoneTreeStore store, ProjectionCheckpointRetentionState original, CancellationToken token)
    {
        var source = original.Source;
        var refused = owner.ApplyEmbedded(original.Probe, token);
        await ProjectionCheckpointRetentionAssertions.PinnedAsync(refused);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(owner, source);
        var completed = owner.ApplyEmbedded(source.Complete, token);
        var effect = completed.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(source.Complete, effect.Receipt,
            new MutationReceipt(PutKind, ProjectionConsumerIdentityFixture.Target, ProjectionCheckpointRetentionAssertions.Effect, 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(effect, new ProjectionBatchResult(effect.Receipt, false, 2L));
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(source.Consumer, source.Definition, 2L, false), [], 3L, batch.Token, false));
        var id = Guid.NewGuid();
        var filtered = ProjectionConsumerIdentityFixture.Operation(OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, source.Consumer, batch.Token, []));
        var advanced = owner.ApplyEmbedded(filtered, token);
        var advance = advanced.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(filtered, advance.Receipt);
        await ProjectionConsumerIdentityAssertions.ExactAsync(advance, new ProjectionBatchResult(advance.Receipt, false, 3L));
        var purge = ProjectionCheckpointRetentionFixture.Purge(source.Partition, 3L, 3);
        var purged = owner.ApplyEmbedded(purge, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(purged.Get<OutboxHead>(), new OutboxHead(3L, 4L, 0L, 0L));
        await ProjectionCheckpointRetentionAssertions.ReclaimedAsync(owner, source);
        var healthyId = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionConsumerIdentityFixture.Source, ProjectionCheckpointRetentionAssertions.HealthySource,
            ProjectionCheckpointRetentionAssertions.HealthyJson, 0L);
        var healthy = ProjectionConsumerIdentityFixture.Operation(OperationKind.Batch, healthyId,
            new CommandRequest(healthyId, source.Partition, [mutation])) with
        {
            EvaluatedAt = source.Time
        };
        token.ThrowIfCancellationRequested();
        var produced = FeedNativeHealthyAdmission.Submit(owner, healthy, explicitTime: true, token);
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(healthy, produced.Get<CommitReceipt>(),
            new MutationReceipt(PutKind, ProjectionConsumerIdentityFixture.Source, ProjectionCheckpointRetentionAssertions.HealthySource, 1L));
        var provisional = new ProjectionCheckpointRetentionContinuation(original, refused, completed, filtered, advanced,
            purge, purged, healthy, produced, [], 0L);
        await ProjectionCheckpointRetentionHealthy.AssertAsync(owner, store, provisional, token);
        await ReplayAsync(owner, store, provisional, token);
        return provisional with
        {
            Image = ProjectionConsumerIdentityAssertions.Image(store),
            Position = store.Position
        };
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionCheckpointRetentionContinuation state, CancellationToken token)
    {
        var source = state.Original.Source;
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source.Seed, source.Seeded, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source.Configure, source.Configured, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Original.Pinned, state.Original.PinFailure, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Original.Probe, state.ProbeFailure, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source.Complete, state.Completed, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Filtered, state.Advanced, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Purge, state.Purged, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Healthy, state.Produced, token);
    }

    private const string PutKind = "putDocument";
}
