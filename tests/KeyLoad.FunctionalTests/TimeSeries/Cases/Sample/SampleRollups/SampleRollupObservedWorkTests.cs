namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleRollupObservedWorkTests
{
    private const int PointThreshold = 3;
    [Test]
    public async Task AcSeries023OriginalCancellationAfterActualWorkHasNoResultOrStoreEffect()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        var triggered = false;
        using var observed = new ReadObservedStore(db.Store, () =>
        {
            if (!triggered && db.Store.GetReadDiagnostics().BorrowedPointLookups >=
                before.BorrowedPointLookups + PointThreshold)
            {
                triggered = true;
                cancellation.Cancel();
            }
        });
        var owner = db.CreateConfiguredDatabase(observed);
        SampleRollupResult? result = null;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => result = owner.ReadSampleRollup(
            SampleRollupWholeFlow.Root, new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
                SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End), cancellation.Token));
        var after = db.Store.GetReadDiagnostics();
        await Assert.That(triggered).IsTrue();
        await Assert.That(result).IsNull();
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsGreaterThanOrEqualTo(PointThreshold);
        await Assert.That(after.PointExaminedBytes).IsGreaterThan(before.PointExaminedBytes);
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
