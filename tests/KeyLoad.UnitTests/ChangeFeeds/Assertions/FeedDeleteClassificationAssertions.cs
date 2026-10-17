using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedDeleteClassificationAssertions
{
    private const string CorruptionDetail = "The committed document mutation does not match its outbox receipt.";

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedDeleteClassificationState state, CancellationToken token)
    {
        var image = FeedVertexAuthorityAssertions.Image(store);
        var position = store.Position;
        var source = state.Source;
        var feed = Assert.ThrowsExactly<KeyLoadException>(() => owner.ReadChangeFeed(FeedVertexAuthorityFixture.Root,
            new(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, source.FeedCursor), token));
        await ErrorAsync(feed);
        await FeedVertexAuthorityAssertions.SameImageAsync(store, image, position);
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var live = Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery(FeedVertexAuthorityFixture.Root,
            new(source.Query, source.LiveCursor), cancellationToken: token));
        await ErrorAsync(live);
        await FeedVertexAuthorityAssertions.SameImageAsync(store, image, position);
    }

    private static async Task ErrorAsync(KeyLoadException error)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
    }

    internal static async Task HealthyAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedDeleteClassificationState state, CommitReceipt? restored, CancellationToken token)
    {
        var source = state.Source;
        var before = FeedVertexAuthorityAssertions.Image(store);
        var cut = store.Position;
        var reference = new EntityRef(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, FeedDeleteClassificationFixture.Id);
        var update = source.UpdateReceipt ?? throw new InvalidOperationException(FeedDeleteClassificationFixture.MissingEntry);
        var changes = new List<DocumentChange>
        {
            Initial(source, 1L, "a", "{\"n\":1}"),
            Initial(source, 2L, FeedDeleteClassificationFixture.Id, "{\"n\":2}"),
            new(3L, update.Token, source.CommittedAt, reference, 2L, false,
                new(reference, 1L, "{\"n\":2}", false, []), new(reference, 2L, "{\"n\":22}", false, [])),
            new(4L, state.Deleted.Token, source.CommittedAt, reference, 3L, true,
                new(reference, 2L, "{\"n\":22}", false, []), null)
        };
        if (restored is not null)
        {
            changes.Add(new(5L, restored.Token, source.CommittedAt, reference, 4L, false, null,
            new(reference, 4L, FeedDeleteClassificationFixture.RestoredJson, false, [])));
        }
        var tail = restored is null ? 4L : 5L;
        var feed = owner.ReadChangeFeed(FeedVertexAuthorityFixture.Root,
            new(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, source.FeedCursor), token);
        await Assert.That(feed.Cursor).IsNotNull();
        await ProjectionConsumerIdentityAssertions.ExactAsync(feed,
            new ChangeFeedPage([.. changes], feed.Cursor, tail, tail, 1L, false, cut));
        var deltas = changes.Select(change => change.Deleted
            ? new LiveQueryChange(change.Sequence, change.Commit, LiveQueryChangeKind.Remove, change.Reference, change.Revision, null)
            : new LiveQueryChange(change.Sequence, change.Commit, LiveQueryChangeKind.Upsert, change.Reference, change.Revision,
                new(change.Reference.Id, change.Revision, change.After!.Json, false, []))).ToArray();
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var live = engine.ReadLiveQuery(FeedVertexAuthorityFixture.Root, new(source.Query, source.LiveCursor), cancellationToken: token);
        await Assert.That(live.Cursor).IsNotNull();
        await ProjectionConsumerIdentityAssertions.ExactAsync(live, new LiveQueryPage([.. deltas], live.Cursor, tail, false, cut));
        var emptyFeed = owner.ReadChangeFeed(FeedVertexAuthorityFixture.Root,
            new(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, feed.Cursor), token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(emptyFeed,
            new ChangeFeedPage([], emptyFeed.Cursor, tail, tail, 1L, false, cut));
        var emptyLive = engine.ReadLiveQuery(FeedVertexAuthorityFixture.Root, new(source.Query, live.Cursor), cancellationToken: token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(emptyLive, new LiveQueryPage([], emptyLive.Cursor, tail, false, cut));
        if (restored is not null)
        {
            await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(FeedVertexAuthorityFixture.Root, reference, restored.Token, token),
            new DocumentResult(reference, 4L, FeedDeleteClassificationFixture.RestoredJson, false, []));
        }
        await FeedVertexAuthorityAssertions.SameImageAsync(store, before, cut);
    }

    private static DocumentChange Initial(FeedVertexAuthorityState source, long sequence, string id, string json)
    {
        var reference = new EntityRef(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, id);
        return new(sequence, source.Receipt.Token, source.CommittedAt, reference, 1L, false, null,
            new(reference, 1L, json, false, []));
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        FeedDeleteClassificationState state, CommandRequest? resurrection, CommitReceipt? restored, CancellationToken token)
    {
        var source = state.Source;
        await FeedVertexAuthorityAssertions.ReplayAsync(owner, store, source.Seed, source.Receipt, source.CommittedAt, token);
        await FeedVertexAuthorityAssertions.ReplayAsync(owner, store,
            source.Update ?? throw new InvalidOperationException(FeedDeleteClassificationFixture.MissingEntry),
            source.UpdateReceipt ?? throw new InvalidOperationException(FeedDeleteClassificationFixture.MissingEntry), source.CommittedAt, token);
        await FeedVertexAuthorityAssertions.ReplayAsync(owner, store, state.Delete, state.Deleted, source.CommittedAt, token);
        if (resurrection is not null && restored is not null)
        {
            await FeedVertexAuthorityAssertions.ReplayAsync(owner, store, resurrection, restored, source.CommittedAt, token);
        }
    }
}
