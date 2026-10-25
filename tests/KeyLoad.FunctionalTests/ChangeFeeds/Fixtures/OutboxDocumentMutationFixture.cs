using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class OutboxDocumentMutationFixture
{
    internal const string Detail = "The committed document mutation does not match its outbox receipt.";
    private const string Space = "outbox";
    private const string ForeignResource = "foreign-document-resource";
    private const string ForeignId = "foreign-document-id";
    private const string PatchKind = "patchDocument";
    private const string Missing = "The complete outbox operation requires its original retained second document.";
    private const long RevisionStep = 1;

    internal static async Task<ProjectionConsumerIdentityState> SeedAsync(TestDatabase database, CancellationToken token)
    {
        var state = await ProjectionConsumerIdentityFixture.SeedAsync(database, token);
        var key = global::KeyLoad.Core.KeySpace.Partition(Space, state.Partition, 2L);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(Missing);
        return state with
        {
            Key = key,
            Original = original
        };
    }

    internal static void Damage(TestDatabase database, ProjectionConsumerIdentityState state, int fault)
    {
        var entry = OrleansSerialization.Deserialize<OutboxEntry>(state.Original);
        var mutation = (PutDocument)entry.Mutation;
        var after = entry.After ?? throw new InvalidOperationException(Missing);
        var damaged = fault switch
        {
            0 => entry with { Mutation = mutation with { Collection = ForeignResource, Resource = ForeignResource } },
            1 => entry with { Mutation = mutation with { Id = ForeignId } },
            2 => entry with { Receipt = entry.Receipt with { Kind = PatchKind } },
            3 => entry with { Receipt = entry.Receipt with { Revision = after.Revision + RevisionStep } },
            4 => entry with { After = after with { Revision = after.Revision + RevisionStep } },
            5 => entry with { After = null },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        database.Store.Commit((transaction, _) => { transaction.PutRecord(state.Key, damaged); return true; });
    }
}
