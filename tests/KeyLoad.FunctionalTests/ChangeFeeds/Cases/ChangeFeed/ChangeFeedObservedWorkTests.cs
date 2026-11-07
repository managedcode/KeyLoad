using KeyLoad.FunctionalTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ChangeFeedObservedWorkTests
{
    private const string Principal = "root";

    [Test]
    public async Task OriginalObservedFeedWorkRejectsWithoutPartialThenCompleteLiteralHealthy()
    {
        using var database = new TestDatabase();
        database.Configure(ChangeFeedLiteralOracle.Collection, ResourceKind.Collection);
        var committedAt = database.Database.EvaluationClock.GetUtcNow();
        var commandId = Guid.NewGuid();
        var receipt = database.Submit(OperationKind.Batch, new CommandRequest(commandId, database.Partition,
            [new PutDocument(ChangeFeedLiteralOracle.Collection,
            ChangeFeedLiteralOracle.FirstId, ChangeFeedLiteralOracle.FirstJson),
            new PutDocument(ChangeFeedLiteralOracle.Collection, ChangeFeedLiteralOracle.SecondId, ChangeFeedLiteralOracle.SecondJson)]),
            id: commandId, time: committedAt).Get<CommitReceipt>();
        var request = new ReadChangeFeedRequest(database.Partition, ChangeFeedLiteralOracle.Collection);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        using var observed = new ReadObservedStore(database.Store);
        var owner = database.CreateConfiguredDatabase(observed);
        observed.CancelAfterReadProgress(cancellation, () => database.Store.GetReadDiagnostics().PointExaminedBytes);
        ChangeFeedPage? partial = null;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            partial = owner.ReadChangeFeed(Principal, request, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(observed.CancelledAfterProgress).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = owner.ReadChangeFeed(Principal, request, TestContext.Current!.Execution.CancellationToken);
        await ChangeFeedLiteralOracle.RequireAsync(database, healthy, receipt, committedAt, position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await ChangeFeedReadCutAssertions.RequireFreshContinuationAsync(database, healthy);
    }
}
