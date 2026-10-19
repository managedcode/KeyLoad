namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedDeleteClassificationFixture
{
    internal const string MissingEntry = "The complete execution deletion flow requires its original retained tombstone entry.";
    internal const string DeleteKind = "deleteDocument";
    internal const string Id = "b";
    internal const string TombstoneJson = "{}";
    internal const string RestoredJson = "{\"n\":222}";

    internal static async Task<FeedDeleteClassificationState> SeedAsync(TestDatabase database, CancellationToken token)
    {
        var source = await FeedVertexAuthorityFixture.SeedAsync(database, historical: true);
        var command = new CommandRequest(Guid.NewGuid(), source.Seed.Partition,
            [new DeleteDocument(FeedVertexAuthorityFixture.Collection, Id, 2L)]);
        token.ThrowIfCancellationRequested();
        var deleted = database.Submit(OperationKind.Batch, command, id: command.CommandId, time: source.CommittedAt).Get<CommitReceipt>();
        await FeedVertexAuthorityAssertions.ReceiptAsync(command, deleted,
            new MutationReceipt(DeleteKind, FeedVertexAuthorityFixture.Collection, Id, 3L));
        var key = global::KeyLoad.Core.KeySpace.Partition(FeedVertexAuthorityFixture.OutboxSpace, source.Seed.Partition, 4L);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingEntry);
        var state = new FeedDeleteClassificationState(source, command, deleted, key, original, [], 0L);
        await FeedDeleteClassificationAssertions.HealthyAsync(database.Database, database.Store, state, null, token);
        var entry = OrleansSerialization.Deserialize<OutboxEntry>(original) ?? throw new InvalidOperationException(MissingEntry);
        var after = entry.After ?? throw new InvalidOperationException(MissingEntry);
        var reference = source.Creation.ExpectedReference(new(source.Seed.Partition, FeedVertexAuthorityFixture.Collection, Id));
        await ProjectionConsumerIdentityAssertions.ExactAsync(entry, new OutboxEntry(4L, 0, deleted.Token,
            source.CommittedAt, new DeleteDocument(reference.Collection, reference.Id, 2L),
            new MutationReceipt(DeleteKind, reference.Collection, reference.Id, 3L),
            new DocumentRecord(reference, 2L, "{\"n\":22}", new RowAccess(), source.CommittedAt),
            new DocumentRecord(reference, 3L, TombstoneJson, new RowAccess(), source.CommittedAt, true)));
        await Assert.That(after.Deleted).IsTrue();
        database.Store.Commit((transaction, _) => { global::KeyLoad.Storage.StorageRecords.PutRecord(transaction, key, entry with { After = after with { Deleted = false } }); return true; });
        state = state with
        {
            DirtyImage = FeedVertexAuthorityAssertions.Image(database.Store),
            DirtyPosition = database.Store.Position
        };
        await FeedDeleteClassificationAssertions.RefuseAsync(database.Database, database.Store, state, token);
        return state;
    }
}
