using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class OutboxDocumentMutationAssertions
{
    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerIdentityState state, CancellationToken token)
    {
        var image = ProjectionConsumerIdentityAssertions.Image(store);
        var position = store.Position;
        await ErrorAsync(() => owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer)));
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
        await ErrorAsync(() => owner.ReadChangeFeed(ProjectionConsumerIdentityFixture.Root,
            new(state.Partition, ProjectionConsumerIdentityFixture.Source), token));
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, state);
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.Partition, ProjectionConsumerIdentityFixture.Target, "effect"), cancellationToken: token)).IsNull();
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, state.Seed, state.Seeded, token);
        await ProjectionConsumerIdentityAssertions.SameAsync(store, image, position);
    }

    private static async Task ErrorAsync(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(OutboxDocumentMutationFixture.Detail);
    }
}
