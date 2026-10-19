using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleMetadataIntegrityScenario
{
    internal static DateTimeOffset Floor => SampleAggregateTestData.Start.AddSeconds(1);
    internal static DateTimeOffset Until => SampleAggregateTestData.Start.AddSeconds(5);
    internal static SampleData Sample(int index)
        => SampleAggregateTestData.Data(index, SampleAggregateTestData.Start.AddSeconds(index - 1), index * 2d);
    internal static SampleRecord Record(int index)
        => new(SampleAggregateTestData.Series, Sample(index), index, SampleAggregateTestData.EmptyTags);
    internal static bool IsRetention(SampleMetadataIntegrityFault fault)
        => fault is SampleMetadataIntegrityFault.RetentionFloor or SampleMetadataIntegrityFault.RetentionCount
            or SampleMetadataIntegrityFault.RetentionVersion or SampleMetadataIntegrityFault.RetentionRoot
            or SampleMetadataIntegrityFault.RetentionWire;
    internal static ErrorCode Code(SampleMetadataIntegrityFault fault)
        => fault is SampleMetadataIntegrityFault.RetentionVersion or SampleMetadataIntegrityFault.BucketVersion
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption;
    internal static byte[] Key(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityFault fault)
        => IsRetention(fault)
            ? SampleRetentionStateReader.Key(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)
            : SampleRollupKeys.Bucket(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until);
    internal static byte[] Original(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityFault fault)
        => fixture.Store.Read(view => view.ReadOwnedValue(Key(fixture, fault)))!;
    internal static void Put(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityFault fault, byte[] bytes)
        => fixture.Store.Commit((tx, _) => { tx.Put(Key(fixture, fault), bytes); return true; });
    internal static void Inject(SampleMetadataIntegrityFixture fixture, SampleMetadataIntegrityFault fault, byte[] original)
    {
        byte[] bytes;
        if (IsRetention(fault))
        {
            var state = OrleansSerialization.Deserialize<SampleRetentionState>(original);
            bytes = fault switch
            {
                SampleMetadataIntegrityFault.RetentionFloor => OrleansSerialization.Serialize(state with { BeforeUtcTicks = -1 }),
                SampleMetadataIntegrityFault.RetentionCount => OrleansSerialization.Serialize(state with { PurgedCount = -1 }),
                SampleMetadataIntegrityFault.RetentionVersion => OrleansSerialization.Serialize(state with { FormatVersion = 2 }),
                SampleMetadataIntegrityFault.RetentionRoot => OrleansSerialization.Serialize("wrong-root"),
                SampleMetadataIntegrityFault.RetentionWire => [byte.MaxValue],
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
        }
        else
        {
            var state = OrleansSerialization.Deserialize<SampleRollupState>(original);
            bytes = fault switch
            {
                SampleMetadataIntegrityFault.BucketEndpoint => OrleansSerialization.Serialize(state with { FromUtcTicks = Floor.UtcTicks + 1 }),
                SampleMetadataIntegrityFault.BucketCount => OrleansSerialization.Serialize(state with { Count = -1 }),
                SampleMetadataIntegrityFault.BucketSequence => OrleansSerialization.Serialize(state with { SourceSequence = -1 }),
                SampleMetadataIntegrityFault.BucketFloor => OrleansSerialization.Serialize(state with { RetentionBeforeUtcTicks = -1 }),
                SampleMetadataIntegrityFault.BucketVersion => OrleansSerialization.Serialize(state with { FormatVersion = 2 }),
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
        }
        Put(fixture, fault, bytes);
    }
    internal static RefreshSampleRollup Refresh(long revision)
        => new(SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until, revision);
    internal static SampleRollupResult Bucket(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.ReadSampleRollup(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until));
    internal static SampleRecord[] Range(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.ReadSamples(SampleAggregateTestData.RootPrincipal, fixture.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until.AddTicks(-1));
    internal static SampleAggregate Aggregate(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until));
    internal static SampleAggregateWindowsResult Windows(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, Floor, Until, TimeSpan.FromSeconds(4)));
    internal static LatestSampleResult Latest(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, Until));
    internal static SampleRetentionStatus Retention(SampleMetadataIntegrityFixture fixture)
        => fixture.Owner.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(fixture.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
}
