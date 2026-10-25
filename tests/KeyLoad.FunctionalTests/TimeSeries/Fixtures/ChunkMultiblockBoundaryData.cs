using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryData
{
    internal const int AdditionalRecords = 254;
    internal const int InitialCount = 257;
    internal const int HealthyCount = 258;
    internal const long InitialRevision = 260;
    internal const long HealthyRevision = 262;
    internal const long InitialGeneration = 2;
    internal const long HealthyGeneration = 3;
    private const string Prefix = "block-boundary-";
    private const string NumberFormat = "D3";
    private const double Value = 2;
    private const int OffsetHours = -5;
    private const long SequenceStart = 4;

    internal static ImmutableArray<SampleData> Additional => [.. Enumerable.Range(0, AdditionalRecords)
        .Select(index => new SampleData(Prefix + index.ToString(NumberFormat, CultureInfo.InvariantCulture),
            index % 2 == 0 ? SampleChunkCanonicalFixture.Start : SampleChunkCanonicalFixture.Start.ToOffset(TimeSpan.FromHours(OffsetHours)), Value))];

    internal static SampleRecord[] Expected(bool healthy)
    {
        var added = Additional.Select((sample, index) => new SampleRecord(SampleChunkCanonicalFixture.Series,
            sample, SequenceStart + index, SampleChunkCanonicalFixture.Tags));
        var original = SampleChunkCanonicalFixture.Expected(late: true).Concat(added);
        if (healthy)
        {
            original = original.Append(new(SampleChunkCanonicalFixture.Series, ChunkSelectedIntegrityFixture.Fresh,
            HealthyCount, SampleChunkCanonicalFixture.Tags));
        }
        return original.OrderBy(record => record.Sample.Timestamp.UtcTicks).ThenBy(record => record.Sequence).ToArray();
    }
}
