namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleRollupObservedWorkTests
{
    private const int PointThreshold = 3;
    [Test]
    public async Task AcSeries023OriginalCancellationAfterActualWorkHasNoResultOrStoreEffect()
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
            before.BorrowedPointLookups + PointThreshold, cancellation.Cancel);
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
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsGreaterThanOrEqualTo(PointThreshold);
        await Assert.That(after.PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
