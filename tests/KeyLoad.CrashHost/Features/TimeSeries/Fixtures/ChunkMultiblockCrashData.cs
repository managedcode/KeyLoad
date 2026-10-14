using System.Globalization;

namespace KeyLoad.CrashHost;

internal static class ChunkMultiblockCrashData
{
    private const int AdditionalCount = 254;
    private const int PreparedAdditionalCount = 252;
    private const int FirstOrdinal = 0;
    private const int TimestampParityPeriod = 2;
    private const int FirstTimestampParity = 0;
    private const long FirstSequence = 1;
    private const long EqualSequence = 2;
    private const long LateSequence = 3;
    private const long AdditionalSequence = 4;
    private const string Prefix = "process-block-boundary-";
    private const string NumberFormat = "D3";
    private const string FreshId = "process-block-fresh";
    private const double AdditionalValue = 2;
    private const double FreshValue = 8;
    private const int FreshTicks = 2;
    private const string AdditionalIdText = "5b75317b-4d36-4410-b28b-207800000004";
    private const string FreshIdText = "5b75317b-4d36-4410-b28b-207800000005";
    private const string MergeIdText = "5b75317b-4d36-4410-b28b-207800000006";
    internal static readonly Guid AdditionalId = Guid.Parse(AdditionalIdText);
    internal static readonly Guid FreshCommandId = Guid.Parse(FreshIdText);
    internal static readonly Guid HealthyMergeId = Guid.Parse(MergeIdText);
    internal static SampleData Fresh => new(FreshId, SampleChunkCrashContract.From.AddTicks(FreshTicks), FreshValue);
    internal static SampleData[] Additional => [.. Enumerable.Range(FirstOrdinal, AdditionalCount).Select(index =>
        new SampleData(Prefix + index.ToString(NumberFormat, CultureInfo.InvariantCulture), index % TimestampParityPeriod == FirstTimestampParity
            ? SampleChunkCrashContract.From : SampleChunkCrashContract.Equal.Timestamp, AdditionalValue))];

    internal static SampleData[] AdditionalFor(bool merge) => merge ? Additional : [SampleChunkCrashContract.Late, .. Additional.Take(PreparedAdditionalCount)];

    internal static SampleData[] AutomaticSealTail => [.. Additional.Skip(PreparedAdditionalCount)];

    internal static SampleRecord[] Expected(bool healthy, bool prepared = false, bool merge = true)
    {
        SampleRecord[] original = [new(SampleChunkCrashContract.Series, SampleChunkCrashContract.First, FirstSequence, SampleChunkCrashContract.Tags),
            new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Equal, EqualSequence, SampleChunkCrashContract.Tags),
            new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Late, LateSequence, SampleChunkCrashContract.Tags)];
        var rows = original.Concat(Additional.Select((sample, index) =>
            new SampleRecord(SampleChunkCrashContract.Series, sample, AdditionalSequence + index, SampleChunkCrashContract.Tags)));
        if (healthy)
        {
            rows = rows.Append(new(SampleChunkCrashContract.Series, Fresh,
            ChunkMultiblockCrashProtocol.HealthySequence, SampleChunkCrashContract.Tags));
        }
        if (prepared && !merge)
        { rows = rows.Where(record => record.Sequence <= ChunkMultiblockCrashProtocol.PreparedAutomaticSealSequence); }
        return rows.OrderBy(record => record.Sample.Timestamp.UtcTicks).ThenBy(record => record.Sequence).ToArray();
    }
}
