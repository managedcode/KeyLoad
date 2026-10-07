using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupPolicyTests
{
    private const string Reader = "rollup-reader";
    [Test]
    public async Task AcSeries023FreshAuthorizationDenialRetainsRawStatsAndHealthyOwner()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        SampleAggregateTestData.GrantRead(db, Reader);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db, Reader), 1, 6, 3, 12, 2, 6, 4);
        var raw = SampleRollupWholeFlow.Raw(db);
        var stats = SampleRollupWholeFlow.Rollups(db);
        var id = Guid.NewGuid();
        var failure = SampleRollupWholeFlow.Commit(db, id, SampleRollupWholeFlow.Refresh(1), Reader);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Json).IsNull();
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
        await Assert.That(SampleRollupWholeFlow.Rollups(db)).IsEqualTo(stats);
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var retry = SampleRollupWholeFlow.Commit(db, id, SampleRollupWholeFlow.Refresh(1), Reader);
        await Assert.That(SampleRollupWholeFlow.Outcome(retry)).IsEqualTo(SampleRollupWholeFlow.Outcome(failure));
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(Reader, db.Partition.TenantId, [], []) { PolicyEpoch = 2 }));
        image = SampleRollupWholeFlow.Image(db);
        position = db.Store.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => SampleRollupWholeFlow.Read(db, Reader));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Commit(SampleRollupWholeFlow.Refresh(1));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 2, 6, 3, 12, 2, 6, 4);
    }
    [Test]
    public async Task AcSeries022RetentionInvalidatesBucketAndRejectsExpiredRebuildWithoutEffects()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var floor = SampleRollupWholeFlow.Start.AddSeconds(30);
        db.Commit(new ExpireSamples(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, floor, 10));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => SampleRollupWholeFlow.Read(db));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await SampleRollupWholeFlow.FailedRetry(db, SampleRollupWholeFlow.Refresh(1),
            ErrorCode.HistoryUnavailable, SampleRollupProtocol.Stale);
        db.Commit(new RefreshSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            floor, SampleRollupWholeFlow.End, 0));
        var healthy = db.Database.ReadSampleRollup(SampleRollupWholeFlow.Root,
            new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, floor, SampleRollupWholeFlow.End));
        await Assert.That(healthy).IsEqualTo(new SampleRollupResult(1,
            new(floor, SampleRollupWholeFlow.End, 6, floor, new(2, 10, 4, 6, 5))));
    }
    [Test]
    public async Task AcSeries021EmptyOffsetEquivalentBucketAndInvalidRangeAreStrict()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        var from = SampleRollupWholeFlow.End.AddMinutes(2);
        var until = from.AddMinutes(1);
        db.Commit(new RefreshSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            from.ToOffset(TimeSpan.FromHours(5)), until.ToOffset(TimeSpan.FromHours(5)), 0));
        var actual = db.Database.ReadSampleRollup(SampleRollupWholeFlow.Root,
            new(db.Partition, SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, from, until));
        await Assert.That(actual).IsEqualTo(new SampleRollupResult(1, new(from, until, 6, null, new(0, 0, null, null, null))));
        await SampleRollupWholeFlow.FailedRetry(db, new(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            from, from, 0), ErrorCode.Validation, SampleRollupProtocol.InvalidRange);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 1, 6, 3, 12, 2, 6, 4);
    }
}
