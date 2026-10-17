using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupIntegrityTests
{
    private const string FiniteFailure = "The time-series aggregate is not finite.";
    private const int UnsupportedVersion = 2;
    [Test]
    public async Task AcSeries023FiniteOverflowRejectsAtomicStatsWithExactRetryAndHealthyEmptyBucket()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, new SampleData("max-a", SampleRollupWholeFlow.Start, double.MaxValue),
            new SampleData("max-b", SampleRollupWholeFlow.Start.AddTicks(1), double.MaxValue));
        await SampleRollupWholeFlow.FailedRetry(db, SampleRollupWholeFlow.Refresh(0), ErrorCode.Validation, FiniteFailure);
        var from = SampleRollupWholeFlow.End;
        var until = from.AddMinutes(1);
        db.Commit(new RefreshSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, from, until, 0));
        var healthy = db.Database.ReadSampleRollup(SampleRollupWholeFlow.Root,
            new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, from, until));
        await Assert.That(healthy).IsEqualTo(new SampleRollupResult(1, new(from, until, 2, null, new(0, 0, null, null, null))));
    }
    [Test]
    public async Task AcSeries023UnsupportedNativeBucketFailsClosedWithoutReadEffectsAndRestoredReadIsHealthy()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var key = SampleRollupKeys.Bucket(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End);
        var original = db.Store.Read(view => SampleRollupRecords.Read(view, db.Partition, SampleRollupWholeFlow.Set,
            SampleRollupWholeFlow.Series, SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End))!;
        db.Store.Commit((tx, _) => { tx.PutRecord(key, original with { FormatVersion = UnsupportedVersion }); return 0; });
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => SampleRollupWholeFlow.Read(db));
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(unsupported.Message).IsEqualTo(SampleRollupProtocol.Unsupported);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Store.Commit((tx, _) => { tx.PutRecord(key, original with { Count = -1 }); return 0; });
        image = SampleRollupWholeFlow.Image(db);
        position = db.Store.Position;
        var corrupt = Assert.ThrowsExactly<KeyLoadException>(() => SampleRollupWholeFlow.Read(db));
        await Assert.That(corrupt.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(corrupt.Message).IsEqualTo(SampleRollupProtocol.Corrupt);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Store.Commit((tx, _) => { tx.PutRecord(key, original); return 0; });
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
