using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupWholeFlowTests
{
    [Test]
    public async Task AcSeries021022LiteralBucketsLateCorrectionReplayAndDrop()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        var raw = SampleRollupWholeFlow.Raw(db);
        var id = Guid.NewGuid();
        var first = SampleRollupWholeFlow.Commit(db, id, SampleRollupWholeFlow.Refresh(0));
        first.Get<CommitReceipt>();
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var retry = SampleRollupWholeFlow.Commit(db, id, SampleRollupWholeFlow.Refresh(0));
        await Assert.That(SampleRollupWholeFlow.Outcome(retry)).IsEqualTo(SampleRollupWholeFlow.Outcome(first));
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
        var secondFrom = SampleRollupWholeFlow.End;
        var secondUntil = secondFrom.AddMinutes(1);
        db.Commit(new RefreshSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, secondFrom, secondUntil, 0));
        var second = db.Database.ReadSampleRollup(SampleRollupWholeFlow.Root,
            new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, secondFrom, secondUntil));
        await Assert.That(second).IsEqualTo(new SampleRollupResult(1,
            new(secondFrom, secondUntil, 6, null, new(1, 8, 8, 8, 8))));
        SampleAggregateTestData.Append(db, new SampleData("late", SampleRollupWholeFlow.Start.AddSeconds(15), 10));
        var staleImage = SampleRollupWholeFlow.Image(db);
        var stalePosition = db.Store.Position;
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => SampleRollupWholeFlow.Read(db));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(stale.Message).IsEqualTo(SampleRollupProtocol.Stale);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(staleImage);
        await Assert.That(db.Store.Position).IsEqualTo(stalePosition);
        db.Commit(SampleRollupWholeFlow.Refresh(1));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 7, 4, 22, 2, 10, 5.5);
        SampleAggregateTestData.Append(db, new SampleData("late", SampleRollupWholeFlow.Start.AddSeconds(15), 10));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 7, 4, 22, 2, 10, 5.5);
        var rawBeforeDrop = SampleRollupWholeFlow.Raw(db);
        db.Commit(new DropSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End, 2));
        await Assert.That(SampleRollupWholeFlow.Read(db)).IsEqualTo(new SampleRollupResult(3, null));
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(rawBeforeDrop);
        db.Commit(SampleRollupWholeFlow.Refresh(3));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 4, 7, 4, 22, 2, 10, 5.5);
    }

    [Test]
    public async Task AcSeries023ExactRawCapAndRevisionFailuresRetainOutcomeAndHealthyCorrection()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        await SampleRollupWholeFlow.FailedRetry(db, SampleRollupWholeFlow.Refresh(0, 2),
            ErrorCode.BudgetExceeded, SampleAggregateReader.SampleBudgetExceeded);
        db.Commit(SampleRollupWholeFlow.Refresh(0, 3));
        await SampleRollupWholeFlow.FailedRetry(db, SampleRollupWholeFlow.Refresh(0),
            ErrorCode.RevisionConflict, SampleRollupProtocol.WrongRevision);
        db.Commit(SampleRollupWholeFlow.Refresh(1));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 6, 3, 12, 2, 6, 4);
    }

    [Test]
    public async Task AcSeries023OriginalReadCancellationPreservesCompleteStateAndHealthyResult()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => db.Database.ReadSampleRollup(
            SampleRollupWholeFlow.Root, new(db.Partition, SampleRollupWholeFlow.Set,
                SampleRollupWholeFlow.Series, SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End), cancellation.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
