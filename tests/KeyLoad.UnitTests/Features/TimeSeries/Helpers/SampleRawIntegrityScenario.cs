using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRawIntegrityScenario
{
    private const string SampleSpace = "sample";
    private const string OtherSeries = "wrong-series";
    private const string WrongRoot = "wrong-native-root";
    private const int DamagedIndex = 3;
    private const int NextSequence = 4;
    private const long InvalidSequence = 0;
    private const int NextTimestampTick = 1;
    internal static DateTimeOffset InitialFloor => SampleAggregateTestData.Start.AddSeconds(1);
    internal static DateTimeOffset FinalFloor => SampleAggregateTestData.Start.AddSeconds(3);
    internal static DateTimeOffset InitialUntil => FinalFloor;
    internal static DateTimeOffset FinalUntil => SampleAggregateTestData.Start.AddSeconds(5);
    internal static TimeSpan Width => TimeSpan.FromSeconds(2);

    internal static SampleData Sample(int index)
        => SampleAggregateTestData.Data(index, SampleAggregateTestData.Start.AddSeconds(index - 1), index * 2d);

    internal static SampleRecord Record(int index)
        => new(SampleAggregateTestData.Series, Sample(index), index, SampleAggregateTestData.EmptyTags);

    internal static byte[] DamagedKey(PartitionRef partition)
        => global::KeyLoad.Core.KeySpace.Partition(SampleSpace, partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            Sample(DamagedIndex).Timestamp, (long)DamagedIndex);

    internal static byte[] OriginalBytes(SampleRawIntegrityFixture fixture)
        => fixture.Store.Read(view => view.ReadOwnedValue(DamagedKey(fixture.Partition)))!;

    internal static void Inject(SampleRawIntegrityFixture fixture, SampleRawIntegrityFault fault)
    {
        var expected = Record(DamagedIndex);
        var bytes = fault switch
        {
            SampleRawIntegrityFault.Series => NativeSerialization.Serialize(expected with { SeriesId = OtherSeries }),
            SampleRawIntegrityFault.Timestamp => NativeSerialization.Serialize(expected with
            { Sample = expected.Sample with { Timestamp = expected.Sample.Timestamp.AddTicks(NextTimestampTick) } }),
            SampleRawIntegrityFault.Sequence => NativeSerialization.Serialize(expected with { Sequence = NextSequence }),
            SampleRawIntegrityFault.NonpositiveSequence => NativeSerialization.Serialize(expected with { Sequence = InvalidSequence }),
            SampleRawIntegrityFault.NonfiniteValue => NativeSerialization.Serialize(expected with
            { Sample = expected.Sample with { Value = double.NaN } }),
            SampleRawIntegrityFault.MissingSample => NativeGraphFixtures.EncodeUnchecked(expected with { Sample = null! }),
            SampleRawIntegrityFault.WrongNativeRoot => NativeSerialization.Serialize(WrongRoot),
            SampleRawIntegrityFault.MalformedNative => [byte.MaxValue],
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Restore(fixture, bytes);
    }

    internal static void Restore(SampleRawIntegrityFixture fixture, byte[] original)
        => fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(DamagedKey(fixture.Partition), original);
            return true;
        });

    internal static ReplicatedOperation Expiry(SampleRawIntegrityFixture fixture)
        => fixture.Operation(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, FinalFloor, 2));

    internal static ReadSamplesRequest Range(SampleRawIntegrityFixture fixture, bool final)
        => new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            final ? FinalFloor : InitialFloor, (final ? FinalUntil : InitialUntil).AddTicks(-1));

    internal static SampleRecord[] ReadRange(SampleRawIntegrityFixture fixture, bool final)
    {
        var request = Range(fixture, final);
        return fixture.Owner.ReadSamples(SampleAggregateTestData.RootPrincipal, request.Partition,
            request.Set, request.SeriesId, request.From, request.Until, request.Limit);
    }

    internal static ReadLatestSampleRequest Latest(SampleRawIntegrityFixture fixture, bool final)
        => new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            Sample(final ? 5 : DamagedIndex).Timestamp);

    internal static AggregateSamplesRequest Aggregate(SampleRawIntegrityFixture fixture, bool final)
        => new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            final ? FinalFloor : InitialFloor, final ? FinalUntil : InitialUntil);

    internal static AggregateSampleWindowsRequest Windows(SampleRawIntegrityFixture fixture, bool final)
        => new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            final ? FinalFloor : InitialFloor, final ? FinalUntil : InitialUntil, Width);
}
