using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkCorrectionTagRows
{
    private const long TaggedTicks = 2;
    private const long TaggedSequence = 4;
    private const long CorrectedRevision = 7;
    private const long MergedRevision = 8;
    private const long CorrectedGeneration = 2;
    private const long MergedGeneration = 3;
    private const double TaggedValue = 8;
    private const string Pointer = "/visible";
    private const string TaggedScalar = "23";
    private const string AbsentScalar = "99";
    private const string Exhausted = "The execution series chunk window exceeds its admitted bound.";
    internal const string TaggedJson = "{\"visible\":\"23\"}";
    internal static SampleData Tagged => new("new-correction-tag", SampleChunkCanonicalFixture.Start.AddTicks(TaggedTicks), TaggedValue);

    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, bool merged)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => PageAsync(database, store, fixture, merged, tag: TaggedScalar), failures);
        await ServerFailureObserver.ObserveAsync(() => PageAsync(database, store, fixture, merged, tag: AbsentScalar), failures);
        await ServerFailureObserver.ObserveAsync(() => PageAsync(database, store, fixture, merged, tag: null), failures);
        await ServerFailureObserver.ObserveAsync(() => RefuseAsync(database, store, fixture), failures);
        await ServerFailureObserver.ObserveAsync(() => PageAsync(database, store, fixture, merged, tag: null), failures);
        await ServerFailureObserver.ObserveAsync(() => RawAsync(database, store, fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static SampleRecord[] All()
        => [.. SampleChunkCanonicalFixture.Expected(late: true),
            new(SampleChunkCanonicalFixture.Series, Tagged, TaggedSequence, TaggedJson)];

    private static async Task PageAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, bool merged, string? tag)
    {
        await ObserveAsync(store, async () =>
        {
            var rows = tag is null ? All() : tag == TaggedScalar ? All()[^1..] : [];
            var request = fixture.Request with
            {
                TagPointer = tag is null ? null : Pointer,
                TagValue = tag,
                Limit = tag is null ? SampleChunkCanonicalFixture.OutputLimit : 1
            };
            var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until, merged ? MergedGeneration : CorrectedGeneration,
                merged ? MergedRevision : CorrectedRevision, TaggedSequence, null, [.. rows], store.Position);
            await EqualAsync(expected, database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request));
        });
    }

    private static async Task RefuseAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture)
    {
        await ObserveAsync(store, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadSampleChunkWindow(
                SampleChunkCanonicalFixture.Principal, fixture.Request with
                {
                    Limit = 1
                }));
            await EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded, Exhausted), failure.ToProblem());
        });
        await ObserveAsync(store, () => RawAsync(database, store, fixture));
    }

    private static Task RawAsync(DatabaseEngine database, ZoneTreeStore store, SampleChunkCanonicalFixture fixture)
        => ObserveAsync(store, async () =>
        {
            var actual = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
            await EqualAsync(All(), actual);
            await EqualAsync(All().Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray(),
                actual.Select(row => BitConverter.DoubleToInt64Bits(row.Sample.Value)).ToArray());
        });

    private static async Task ObserveAsync(ZoneTreeStore store, Func<Task> primary)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(primary, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(
            store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
