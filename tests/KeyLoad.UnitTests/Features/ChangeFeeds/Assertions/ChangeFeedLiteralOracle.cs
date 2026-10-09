namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ChangeFeedLiteralOracle
{
    internal const string Collection = "orders";
    internal const string FirstId = "a";
    internal const string SecondId = "b";
    internal const string FirstJson = "{\"n\":1}";
    internal const string SecondJson = "{\"n\":2}";
    private const string Principal = "root";
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long FirstRevision = 1;

    internal static async Task RequireAsync(TestDatabase database, ChangeFeedPage page, CommitReceipt receipt,
        DateTimeOffset committedAt, long position)
    {
        DocumentChange[] literal =
        [
            Change(database.Partition, FirstId, FirstJson, FirstSequence, receipt, committedAt),
            Change(database.Partition, SecondId, SecondJson, SecondSequence, receipt, committedAt)
        ];
        await Assert.That(JsonDefaults.Serialize(page.Changes).SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
        await Assert.That(page.ThroughSequence).IsEqualTo(SecondSequence);
        await Assert.That(page.Tail).IsEqualTo(SecondSequence);
        await Assert.That(page.FirstAvailable).IsEqualTo(FirstSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        var continuation = database.Database.ReadChangeFeed(Principal,
            new(database.Partition, Collection, page.Cursor), TestContext.Current!.Execution.CancellationToken);
        var empty = new ChangeFeedPage([], continuation.Cursor, SecondSequence, SecondSequence,
            FirstSequence, false, position);
        await Assert.That(JsonDefaults.Serialize(continuation).SequenceEqual(JsonDefaults.Serialize(empty))).IsTrue();
        // The deliberate existing API reaches the same complete authorized operation.
        var originalApi = database.Database.ReadChangeFeed(Principal, new(database.Partition, Collection));
        await Assert.That(JsonDefaults.Serialize(originalApi.Changes).SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
    }

    private static DocumentChange Change(PartitionRef partition, string id, string json, long sequence,
        CommitReceipt receipt, DateTimeOffset committedAt)
    {
        var reference = new EntityRef(partition, Collection, id);
        return new(sequence, receipt.Token, committedAt, reference, FirstRevision, false, null,
            new(reference, FirstRevision, json, false, []));
    }
}
