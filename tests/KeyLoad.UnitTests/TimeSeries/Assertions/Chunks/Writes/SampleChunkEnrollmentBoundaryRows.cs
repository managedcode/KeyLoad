using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkEnrollmentBoundaryRows
{
    private const int WindowHours = 1;
    private const double RightValue = 8;
    private const long OpenGeneration = 0;
    private const long SealedGeneration = 1;
    internal const long AppendedRevision = 2;
    internal const long SealedRevision = 3;
    internal const long LeftSequence = 3;
    internal const long RightSequence = 4;
    internal static DateTimeOffset Until => SampleChunkCanonicalFixture.Until.AddHours(WindowHours);
    internal static DateTimeOffset RawUntil => Until.AddHours(WindowHours);
    internal static SampleData Left => new("enrollment-left", SampleChunkCanonicalFixture.Until, -0d);
    internal static SampleData Right => new("enrollment-right", Until, RightValue);

    internal static async Task CurrentAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, Guid window, bool populated, bool sealedWindow)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var rows = populated ? new[] { new SampleRecord(SampleChunkCanonicalFixture.Series, Left, LeftSequence,
                SampleChunkCanonicalFixture.Tags) } : [];
            var expected = new SampleChunkWindowResult(window, SampleChunkCanonicalFixture.Until,
                Until, sealedWindow ? SealedGeneration : OpenGeneration,
                sealedWindow ? SealedRevision : populated ? AppendedRevision : SampleChunkCanonicalFixture.FirstRevision,
                sealedWindow ? RightSequence : populated ? LeftSequence : SampleChunkCanonicalFixture.InitialSequence,
                null, [.. rows], position);
            var actual = database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
                fixture.Request with
                {
                    WindowId = window
                });
            await EqualAsync(expected, actual);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => RawAsync(database, store, fixture, populated), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RawAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, bool populated)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var expected = populated ? SampleChunkCanonicalFixture.Expected().Concat(
                [new SampleRecord(SampleChunkCanonicalFixture.Series, Left, LeftSequence, SampleChunkCanonicalFixture.Tags),
                 new SampleRecord(SampleChunkCanonicalFixture.Series, Right, RightSequence, SampleChunkCanonicalFixture.Tags)]).ToArray()
                : SampleChunkCanonicalFixture.Expected();
            var actual = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
                RawUntil, SampleChunkCanonicalFixture.OutputLimit);
            await EqualAsync(expected, actual);
            await EqualAsync(expected.Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray(),
                actual.Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray());
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
