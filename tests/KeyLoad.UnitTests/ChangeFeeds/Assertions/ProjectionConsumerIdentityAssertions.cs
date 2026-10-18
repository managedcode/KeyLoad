using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionConsumerIdentityAssertions
{
    internal static string[] Image(ZoneTreeStore store)
        => global::KeyLoad.UnitTests.Features.GraphTraversal.GraphShortestPathFullStoreImage.Bytes(store);

    internal static async Task SameAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(Image(store)).IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerIdentityState state, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        await ErrorAsync(() => owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer)));
        await SameAsync(store, image, position);
        await ErrorAsync(() => owner.GetOutboxStatus(ProjectionConsumerIdentityFixture.Root, state.Partition));
        await SameAsync(store, image, position);
        await ErrorAsync(() => owner.ApplyEmbedded(state.Complete, token));
        await SameAsync(store, image, position);
        await ErrorAsync(() => owner.ApplyEmbedded(state.ConfigureAgain, token));
        await SameAsync(store, image, position);
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(state.Partition, ProjectionConsumerIdentityFixture.Target, "effect"), cancellationToken: token)).IsNull();
    }

    private static async Task ErrorAsync(Action action)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(ProjectionConsumerIdentityFixture.Detail);
    }

    internal static async Task OriginalBatchAsync(DatabaseEngine owner, ProjectionConsumerIdentityState state)
    {
        var batch = owner.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(state.Consumer));
        await Assert.That(batch.Token).IsNotNull();
        var mutations = new PutDocument[]
        { new(ProjectionConsumerIdentityFixture.Source, "a", "{\"n\":1}", 0L), new(ProjectionConsumerIdentityFixture.Source, "b", "{\"n\":2}", 0L) };
        var entries = mutations.Select((mutation, ordinal) => new OutboxEntry(ordinal + 1L, ordinal, state.SeedReceipt.Token, state.Time,
            mutation, new("putDocument", ProjectionConsumerIdentityFixture.Source, mutation.Id, 1L), null,
            new(new(state.Partition, ProjectionConsumerIdentityFixture.Source, mutation.Id), 1L, mutation.Json, new RowAccess(), state.Time))).ToArray();
        await ExactAsync(batch, new ProjectionBatch(new(state.Consumer, state.Definition, 0L, false), [.. entries], 2L, batch.Token, false));
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> expected, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        await ExactAsync(owner.ApplyEmbedded(operation, token), expected);
        await SameAsync(store, image, position);
    }

    internal static async Task ReceiptAsync(Command<DatabaseCommand> operation, CommitReceipt receipt, params MutationReceipt[] expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await ExactAsync(receipt.Mutations, global::System.Collections.Immutable.ImmutableArray.CreateRange(expected));
    }

    internal static async Task SourcesAsync(DatabaseEngine owner, PartitionRef partition)
    {
        await ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root, new(partition, ProjectionConsumerIdentityFixture.Source, "a")),
            new DocumentResult(new(partition, ProjectionConsumerIdentityFixture.Source, "a"), 1L, "{\"n\":1}", false, []));
        await ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root, new(partition, ProjectionConsumerIdentityFixture.Source, "b")),
            new DocumentResult(new(partition, ProjectionConsumerIdentityFixture.Source, "b"), 1L, "{\"n\":2}", false, []));
    }

    internal static async Task EffectAsync(DatabaseEngine owner, PartitionRef partition)
        => await ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(partition, ProjectionConsumerIdentityFixture.Target, "effect")),
            new DocumentResult(new(partition, ProjectionConsumerIdentityFixture.Target, "effect"), 1L, "{\"sourceCount\":2}", false, []));

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
