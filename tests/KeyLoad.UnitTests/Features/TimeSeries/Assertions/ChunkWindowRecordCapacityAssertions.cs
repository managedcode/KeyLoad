using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkWindowRecordCapacityAssertions
{
    private const int ReadLimit = 257;
    private const int WindowHours = 1;
    private const long FreshGeneration = 1;
    private const long FreshRevision = 3;
    private const long FreshSequence = 258;
    private const string Missing = "The native series chunk window is unavailable.";
    private const string MissingRepresentation = "The original complete two-block generation is absent.";

    internal static string[] Capture(ZoneTreeStore store, SampleChunkCanonicalFixture fixture)
        => store.Read(view => Keys(fixture).Select(key =>
        {
            var value = view.ReadOwnedValue(key) ?? throw new InvalidOperationException(MissingRepresentation);
            return Convert.ToHexString(JsonDefaults.Serialize(value));
        }).ToArray());

    internal static byte[][] Keys(SampleChunkCanonicalFixture fixture) =>
        [SampleChunkKeys.Manifest(fixture.Owner.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, ChunkMultiblockBoundaryData.InitialGeneration),
         SampleChunkKeys.Block(fixture.Owner.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, ChunkMultiblockBoundaryData.InitialGeneration, 0),
         SampleChunkKeys.Block(fixture.Owner.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, ChunkMultiblockBoundaryData.InitialGeneration, 1)];

    internal static Task InitialAsync(SampleChunkCanonicalFixture fixture, bool expired)
        => RowsAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, fixture.WindowId, fresh: false, expired);

    internal static Task HealthyAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, Guid freshWindow)
        => RowsAsync(database, store, fixture, freshWindow, fresh: true, expired: true);

    private static async Task RowsAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, Guid windowId, bool fresh, bool expired)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var rows = fresh
            ? new[] { new SampleRecord(SampleChunkCanonicalFixture.Series, ChunkWindowRecordCapacityOperations.Fresh,
                FreshSequence, SampleChunkCanonicalFixture.Tags) }
            : expired ? [] : ChunkMultiblockBoundaryData.Expected(healthy: false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var from = fresh ? SampleChunkCanonicalFixture.Until : SampleChunkCanonicalFixture.Start;
            var until = fresh ? from.AddHours(WindowHours) : SampleChunkCanonicalFixture.Until;
            var expected = new SampleChunkWindowResult(windowId, from, until,
                fresh ? FreshGeneration : ChunkMultiblockBoundaryData.InitialGeneration,
                fresh ? FreshRevision : ChunkMultiblockBoundaryData.InitialRevision,
                fresh ? FreshSequence : ChunkMultiblockBoundaryData.InitialCount,
                expired ? SampleChunkCanonicalFixture.Until.UtcTicks : null, [.. rows], position);
            var actual = database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
                fixture.Request with { WindowId = windowId, Limit = ReadLimit });
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var raw = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until.AddHours(WindowHours), ReadLimit);
            await Assert.That(JsonDefaults.Serialize(raw).SequenceEqual(JsonDefaults.Serialize(rows))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task RetentionAsync(SampleChunkCanonicalFixture fixture, long purged, bool more)
        => RetentionAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, purged, more);

    internal static async Task RetentionAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, long purged, bool more)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = database.ReadSampleRetention(SampleChunkCanonicalFixture.Principal,
                new(fixture.Owner.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series));
            var expected = new SampleRetentionStatus(SampleChunkCanonicalFixture.Until, purged, more);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task PhysicalAsync(ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, long purged, bool fresh)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var expected = fresh
            ? new[] { new SampleRecord(SampleChunkCanonicalFixture.Series, ChunkWindowRecordCapacityOperations.Fresh,
                FreshSequence, SampleChunkCanonicalFixture.Tags) }
            : ChunkMultiblockBoundaryData.Expected(healthy: false).Skip(checked((int)purged)).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var page = store.Read(view => view.Scan(SampleReadKeys.Prefix(fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series), ReadLimit));
            await Assert.That(page.HasMore).IsFalse();
            var records = page.Records.Select(row => NativeSerialization.Deserialize<SampleRecord>(row.Value.Span)).ToArray();
            await Assert.That(JsonDefaults.Serialize(records).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            var keys = expected.Select(row => Convert.ToHexString(KeySpace.Partition("sample", fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, row.Sample.Timestamp, row.Sequence))).ToArray();
            var actualKeys = page.Records.Select(row => Convert.ToHexString(row.Key.Span)).ToArray();
            await Assert.That(JsonDefaults.Serialize(actualKeys).SequenceEqual(JsonDefaults.Serialize(keys))).IsTrue();
            var actualBits = records.Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray();
            var expectedBits = expected.Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray();
            await Assert.That(JsonDefaults.Serialize(actualBits).SequenceEqual(JsonDefaults.Serialize(expectedBits))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RetiredAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadSampleChunkWindow(
                SampleChunkCanonicalFixture.Principal, fixture.Request with { Limit = ReadLimit }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
            await Assert.That(failure.Message).IsEqualTo(Missing);
        }, failures);
        foreach (var key in Keys(fixture))
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsNull(), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
