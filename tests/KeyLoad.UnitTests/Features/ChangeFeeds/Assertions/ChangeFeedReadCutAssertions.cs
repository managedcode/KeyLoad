using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ChangeFeedReadCutAssertions
{
    private const string Principal = "root";
    private const string OtherPrincipal = "read-cut-control";
    private const long FirstSequence = 1;
    private const long TailSequence = 2;

    internal static async Task RequireFreshContinuationAsync(TestDatabase database, ChangeFeedPage original)
    {
        var position = database.Store.Position;
        await Assert.That(position).IsEqualTo(original.CutPosition);
        var principal = new PrincipalRecord(OtherPrincipal, database.Partition.TenantId, [], []);
        var observed = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(observed).SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        var currentPosition = database.Store.Position;
        await Assert.That(currentPosition).IsGreaterThan(position);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var continuation = database.Database.ReadChangeFeed(Principal,
            new(database.Partition, ChangeFeedLiteralOracle.Collection, original.Cursor),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(string.IsNullOrEmpty(continuation.Cursor)).IsFalse();
        var expected = new ChangeFeedPage([], continuation.Cursor, TailSequence, TailSequence,
            FirstSequence, false, currentPosition);
        await Assert.That(JsonDefaults.Serialize(continuation).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(currentPosition);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
