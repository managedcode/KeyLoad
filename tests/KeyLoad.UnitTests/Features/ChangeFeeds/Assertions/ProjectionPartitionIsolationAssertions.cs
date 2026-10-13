using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionPartitionIsolationAssertions
{
    internal static async Task VerifyAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionPartitionIsolationState state, CancellationToken token)
    {
        var image = ProjectionConsumerIdentityAssertions.Image(store);
        var cut = store.Position;
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state.First.Partition);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state.Second.Partition);
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, state.First.Partition),
            new OutboxStatus(new(2L, 3L, 0L, 0L), [new(state.First.Consumer, state.First.Definition, 0L, true)]));
        var healthy = state.HealthySourceReceipt is not null;
        var bytes = healthy ? EntryBytes(store, state.Second.Partition, 4L) + EntryBytes(store, state.Second.Partition, 5L) : 0L;
        var checkpoint = healthy ? 5L : 3L;
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, state.Second.Partition),
            new OutboxStatus(new(checkpoint, 4L, healthy ? 2L : 0L, bytes),
                [new(state.Second.Consumer, state.Second.Definition, checkpoint, false)]));
        var released = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.First.Consumer)));
        await Assert.That(released.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Second.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(state.Second.Consumer, state.Second.Definition, checkpoint, false), [], checkpoint, batch.Token, false));
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.First.Partition, ProjectionConsumerIdentityFixture.Target, "effect"), cancellationToken: token)).IsNull();
        await ProjectionConsumerIdentityAssertions.EffectAsync(owner, state.Second.Partition);
        await DocumentAsync(owner, state.Second.Partition, ProjectionConsumerIdentityFixture.Source,
            ProjectionPartitionIsolationFixture.HealthySource, healthy ? state.HealthySourceReceipt : null, token);
        await DocumentAsync(owner, state.Second.Partition, ProjectionConsumerIdentityFixture.Target,
            ProjectionPartitionIsolationFixture.HealthyEffect, healthy ? state.HealthyEffectReceipt : null, token);
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, cut);
        foreach (var item in state.Outcomes)
        { await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, item.Operation, item.Result, token); }
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, cut);
    }

    private static long EntryBytes(ZoneTreeStore store, PartitionRef partition, long sequence)
        => store.Read(view => (view.ReadOwnedValue(KeySpace.Partition(ProjectionPartitionIsolationFixture.EntrySpace, partition, sequence))
            ?? throw new InvalidOperationException(ProjectionConsumerIdentityFixture.MissingRow)).LongLength);

    private static async Task DocumentAsync(DatabaseEngine owner, PartitionRef partition,
        string collection, string id, CommitReceipt? receipt, CancellationToken token)
    {
        var reference = new EntityRef(partition, collection, id);
        var actual = owner.GetDocument(ProjectionConsumerIdentityFixture.Root, reference, receipt?.Token, token);
        if (receipt is null)
        { await Assert.That(actual).IsNull(); }
        else
        {
            await ProjectionConsumerIdentityAssertions.ExactAsync(actual,
            new DocumentResult(reference, 1L, ProjectionPartitionIsolationFixture.HealthyJson, false, []));
        }
    }

    internal static async Task FailureAsync(OperationResult result, ErrorCode code, string detail)
        => await ProjectionConsumerIdentityAssertions.ExactAsync(result, new OperationResult(null, code, detail));

    internal static async Task OriginalBytesAsync(ZoneTreeStore store, byte[][] keys, string[] expected)
        => await ProjectionConsumerIdentityAssertions.ExactAsync(ProjectionPartitionIsolationFixture.SelectedBytes(store, keys), expected);
}
