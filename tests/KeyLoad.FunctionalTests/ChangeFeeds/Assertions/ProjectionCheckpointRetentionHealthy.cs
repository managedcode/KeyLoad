using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class ProjectionCheckpointRetentionHealthy
{
    private const string OutboxSpace = "outbox";
    private const string MissingEntry = "The healthy retained execution source requires its original committed entry.";
    private const string PutKind = "putDocument";

    internal static async Task AssertAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionCheckpointRetentionContinuation state, CancellationToken token)
    {
        var source = state.Original.Source;
        var receipt = state.Produced.Get<CommitReceipt>();
        var reference = source.Creation.ExpectedReference(new(source.Partition, ProjectionConsumerIdentityFixture.Source, ProjectionCheckpointRetentionAssertions.HealthySource));
        var mutation = new PutDocument(reference.Collection, reference.Id, ProjectionCheckpointRetentionAssertions.HealthyJson, 0L);
        var entry = new OutboxEntry(4L, 0, receipt.Token, source.Time, mutation,
            new MutationReceipt(PutKind, reference.Collection, reference.Id, 1L), null,
            new DocumentRecord(reference, 1L, ProjectionCheckpointRetentionAssertions.HealthyJson, new RowAccess(), source.Time));
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(source.Consumer, source.Definition, 3L, false), [entry], 4L, batch.Token, false));
        var key = global::KeyLoad.Core.KeySpace.Partition(OutboxSpace, source.Partition, 4L);
        var bytes = store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingEntry);
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, source.Partition),
            new OutboxStatus(new(4L, 4L, 1L, bytes.LongLength), [new(source.Consumer, source.Definition, 3L, false)]));
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root, reference, receipt.Token, token),
            new DocumentResult(reference, 1L, ProjectionCheckpointRetentionAssertions.HealthyJson, false, []));
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, source);
        await ProjectionConsumerIdentityAssertions.EffectAsync(owner, source);
    }
}
