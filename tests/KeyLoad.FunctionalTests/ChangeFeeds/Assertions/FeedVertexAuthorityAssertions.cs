using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class FeedVertexAuthorityAssertions
{
    internal const string CorruptionDetail = "The change-feed current document does not match its canonical identity.";
    internal const string HistoricalCorruptionDetail = "The committed document change does not match its canonical identity.";

    internal static string[] Image(ZoneTreeStore store)
        => global::KeyLoad.FunctionalTests.CanonicalStoreImage.Bytes(store);

    internal static async Task SameImageAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(Image(store)).IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedVertexAuthorityState state, CancellationToken token)
        => RefuseAsync(owner, store, state, state.Update is not null ? HistoricalCorruptionDetail : CorruptionDetail, token);

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedVertexAuthorityState state, string expectedDetail, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        var feed = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadChangeFeed(
            FeedVertexAuthorityFixture.Root, new(state.Seed.Partition, FeedVertexAuthorityFixture.Collection, state.FeedCursor), token));
        await ErrorAsync(feed, expectedDetail);
        await SameImageAsync(store, image, position);
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var live = Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery(
            FeedVertexAuthorityFixture.Root, new(state.Query, state.LiveCursor), cancellationToken: token));
        await ErrorAsync(live, expectedDetail);
        await SameImageAsync(store, image, position);
    }

    private static async Task ErrorAsync(KeyLoadException error, string expectedDetail)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(expectedDetail);
    }

    internal static async Task HealthyAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedVertexAuthorityState state, CommitReceipt? continuation, CancellationToken token)
    {
        var image = Image(store);
        var cut = store.Position;
        var changes = new List<DocumentChange>
        {
            Change(state, 1L, "a", "{\"n\":1}", state.Receipt, state.CommittedAt),
            Change(state, 2L, "b", "{\"n\":2}", state.Receipt, state.CommittedAt)
        };
        var tail = 2L;
        if (state.UpdateReceipt is { } update)
        {
            var reference = state.Creation.ExpectedReference(new(state.Seed.Partition, FeedVertexAuthorityFixture.Collection, "b"));
            changes.Add(new(3L, update.Token, state.CommittedAt, reference, 2L, false,
                new(reference, 1L, "{\"n\":2}", false, []), new(reference, 2L, "{\"n\":22}", false, [])));
            tail = 3L;
        }
        if (continuation is not null)
        {
            tail++;
            changes.Add(Change(state, tail, "c", "{\"n\":3}", continuation, state.CommittedAt));
        }
        var feed = owner.ReadChangeFeed(FeedVertexAuthorityFixture.Root,
            new(state.Seed.Partition, FeedVertexAuthorityFixture.Collection, state.FeedCursor), token);
        await Assert.That(feed.Cursor).IsNotNull();
        await ExactAsync(feed, new ChangeFeedPage([.. changes], feed.Cursor, tail,
            tail, 1L, false, cut));
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var live = engine.ReadLiveQuery(FeedVertexAuthorityFixture.Root,
            new(state.Query, state.LiveCursor), cancellationToken: token);
        var deltas = changes.Select(change => new LiveQueryChange(change.Sequence, change.Commit, LiveQueryChangeKind.Upsert,
            change.Reference, change.Revision, new(change.Reference.Id, change.Revision, change.After!.Json, false, []))).ToArray();
        await Assert.That(live.Cursor).IsNotNull();
        await ExactAsync(live, new LiveQueryPage([.. deltas], live.Cursor, tail, false, cut));
        var emptyFeed = owner.ReadChangeFeed(FeedVertexAuthorityFixture.Root,
            new(state.Seed.Partition, FeedVertexAuthorityFixture.Collection, feed.Cursor), token);
        await ExactAsync(emptyFeed, new ChangeFeedPage([], emptyFeed.Cursor, tail,
            tail, 1L, false, cut));
        var emptyLive = engine.ReadLiveQuery(FeedVertexAuthorityFixture.Root,
            new(state.Query, live.Cursor), cancellationToken: token);
        await ExactAsync(emptyLive, new LiveQueryPage([], emptyLive.Cursor, tail, false, cut));
        await SameImageAsync(store, image, cut);
    }

    private static DocumentChange Change(FeedVertexAuthorityState state, long sequence, string id,
        string json, CommitReceipt receipt, DateTimeOffset time)
    {
        var reference = state.Creation.ExpectedReference(new(state.Seed.Partition, FeedVertexAuthorityFixture.Collection, id));
        return new(sequence, receipt.Token, time, reference, 1L, false, null, new(reference, 1L, json, false, []));
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt, DateTimeOffset time, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        var result = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, command.CommandId, FeedVertexAuthorityFixture.Root, time, global::System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options)), token).Get<CommitReceipt>();
        await ExactAsync(result, receipt);
        await SameImageAsync(store, image, position);
    }

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, params MutationReceipt[] expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await ExactAsync(receipt.Mutations, global::System.Collections.Immutable.ImmutableArray.CreateRange(expected));
    }

    private static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
