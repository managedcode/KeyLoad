using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupObservedWorkTests
{
    private const int NativePointThreshold = 3;
    private const int DeadlineSeconds = 1;
    private const int BeyondDeadlineSeconds = 2;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";
    [Test]
    public async Task AcSeries023OriginalCancellationAfterActualNativeWorkHasNoResultOrStoreEffect()
    {
        var clock = new SampleRollupWorkClock();
        using var db = new TestDatabase(timeProvider: clock);
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        clock.Arm(() => db.Store.GetReadDiagnostics().BorrowedPointLookups >=
            before.BorrowedPointLookups + NativePointThreshold, cancellation.Cancel);
        SampleRollupResult? result = null;
        OperationCanceledException error;
        try
        {
            error = Assert.ThrowsExactly<OperationCanceledException>(() => result = db.Database.ReadSampleRollup(
                SampleRollupWholeFlow.Root, new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
                    SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End), cancellation.Token));
        }
        finally { clock.Disarm(); }
        var after = db.Store.GetReadDiagnostics();
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(result).IsNull();
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsGreaterThanOrEqualTo(NativePointThreshold);
        await Assert.That(after.PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
    [Test]
    public async Task AcSeries023ElapsedDeadlineAfterActualNativeWorkHasNoResultOrStoreEffect()
    {
        var clock = new SampleRollupWorkClock();
        using var db = new TestDatabase(new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds }, timeProvider: clock);
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        clock.Arm(() => db.Store.GetReadDiagnostics().BorrowedPointLookups >=
            before.BorrowedPointLookups + NativePointThreshold, () => clock.Advance(TimeSpan.FromSeconds(BeyondDeadlineSeconds)));
        SampleRollupResult? result = null;
        KeyLoadException error;
        try
        { error = Assert.ThrowsExactly<KeyLoadException>(() => result = SampleRollupWholeFlow.Read(db)); }
        finally { clock.Disarm(); }
        var after = db.Store.GetReadDiagnostics();
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(result).IsNull();
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsGreaterThanOrEqualTo(NativePointThreshold);
        await Assert.That(after.PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(DeadlineDetail);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
