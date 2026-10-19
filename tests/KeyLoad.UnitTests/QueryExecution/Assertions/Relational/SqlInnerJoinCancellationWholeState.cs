namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinCancellationWholeState
{
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string Customer = "customer";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const long SeedRevision = 1;

    internal static async Task UnchangedAsync(TestDatabase database, string[] before, long position, string identity)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(database.Store.Identity))).IsEqualTo(identity);
        await Assert.That(global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task LiteralAsync(QueryPage page, string[] ids, long position)
    {
        var rows = ids.Select(id => new QueryRow(id, SeedRevision,
            "{\"order_id\":\"" + id + "\",\"name\":\"Ada\"}", Redacted: false, RedactedFields: [],
            Sources: [new(LeftAlias, id, SeedRevision), new(RightAlias, Customer, SeedRevision)])).ToArray();
        var expected = new QueryPage([.. rows], null, position, AccessPath);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
