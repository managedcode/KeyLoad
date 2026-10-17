using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ChangeFeedObservedWorkTests
{
    private const string Principal = "root";
    private const int DeadlineSeconds = 1;
    private const int ExpiredSeconds = 2;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalObservedNativeFeedWorkRejectsWithoutPartialThenCompleteLiteralHealthy(bool cancel)
    {
        var clock = new QueryObservedWorkClock();
        using var database = new TestDatabase(new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds }, timeProvider: clock);
        database.Configure(ChangeFeedLiteralOracle.Collection, ResourceKind.Collection);
        var committedAt = clock.GetUtcNow();
        var receipt = database.Commit(new PutDocument(ChangeFeedLiteralOracle.Collection,
            ChangeFeedLiteralOracle.FirstId, ChangeFeedLiteralOracle.FirstJson),
            new PutDocument(ChangeFeedLiteralOracle.Collection, ChangeFeedLiteralOracle.SecondId, ChangeFeedLiteralOracle.SecondJson));
        var request = new ReadChangeFeedRequest(database.Partition, ChangeFeedLiteralOracle.Collection);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        clock.Arm(() => database.Store.GetReadDiagnostics().PointExaminedBytes > before.PointExaminedBytes,
            () => { if (cancel) { cancellation.Cancel(); } else { clock.Advance(TimeSpan.FromSeconds(ExpiredSeconds)); } });
        ChangeFeedPage? partial = null;
        try
        {
            if (cancel)
            {
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
                    partial = database.Database.ReadChangeFeed(Principal, request, cancellation.Token));
                await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
            }
            else
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                    partial = database.Database.ReadChangeFeed(Principal, request, cancellation.Token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                await Assert.That(failure.Message).IsEqualTo(DeadlineDetail);
            }
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = database.Database.ReadChangeFeed(Principal, request, TestContext.Current!.Execution.CancellationToken);
        await ChangeFeedLiteralOracle.RequireAsync(database, healthy, receipt, committedAt, position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await ChangeFeedReadCutAssertions.RequireFreshContinuationAsync(database, healthy);
    }
}
