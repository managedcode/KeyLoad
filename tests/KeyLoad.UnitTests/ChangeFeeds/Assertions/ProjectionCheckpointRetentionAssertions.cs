using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionCheckpointRetentionAssertions
{
    internal const string CorruptionDetail = "The committed projection checkpoint is outside its retained outbox range.";
    private const string PinnedDetail = "A projection consumer or rebuild pins the requested outbox history.";

    internal static async Task PinnedAsync(Result<object> result)
        => await ProjectionConsumerIdentityAssertions.ExactAsync(result, Result<object>.Fail(Errors.Problem(ErrorCode.Conflict, PinnedDetail)));

    internal static async Task CorruptAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionCheckpointRetentionState state, CancellationToken token)
    {
        var image = ProjectionConsumerIdentityAssertions.Image(store);
        var position = store.Position;
        var status = Assert.ThrowsExactly<KeyLoadException>(() => owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, state.Source.Partition));
        await ErrorAsync(status);
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
        var purge = Assert.ThrowsExactly<KeyLoadException>(() => owner.ApplyEmbedded(state.Probe, token));
        await ErrorAsync(purge);
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state.Source);
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.Source.Partition, ProjectionConsumerIdentityFixture.Target, Effect), cancellationToken: token)).IsNull();
    }

    private static async Task ErrorAsync(KeyLoadException error)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
    }

    internal static async Task ReclaimedAsync(DatabaseEngine owner, ProjectionConsumerIdentityState source)
    {
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, source.Partition),
            new OutboxStatus(new(3L, 4L, 0L, 0L), [new(source.Consumer, source.Definition, 3L, false)]));
        await ProjectionConsumerIdentityAssertions.EffectAsync(owner, source);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, source);
    }

    internal const string Effect = "effect";
    internal const string HealthySource = "c";
    internal const string HealthyJson = "{\"n\":3}";
}
