namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleChunkEncodingPlan
{
    private const string Empty = "The sample chunk must contain records.";

    internal SampleChunkEncodingPlan(Dictionary<string, int> tagIndexes, string[] tagDictionary,
        int utcTicksBytes, int offsetsBytes, int sequencesBytes, int valuesBytes,
        int seriesBytes, int eventIdsBytes, int tagsBytes, long columnsBytes)
    {
        TagIndexes = tagIndexes;
        TagDictionary = tagDictionary;
        UtcTicksBytes = utcTicksBytes;
        OffsetsBytes = offsetsBytes;
        SequencesBytes = sequencesBytes;
        ValuesBytes = valuesBytes;
        SeriesBytes = seriesBytes;
        EventIdsBytes = eventIdsBytes;
        TagsBytes = tagsBytes;
        ColumnsBytes = columnsBytes;
    }

    internal Dictionary<string, int> TagIndexes { get; }
    internal string[] TagDictionary { get; }
    internal int UtcTicksBytes { get; }
    internal int OffsetsBytes { get; }
    internal int SequencesBytes { get; }
    internal int ValuesBytes { get; }
    internal int SeriesBytes { get; }
    internal int EventIdsBytes { get; }
    internal int TagsBytes { get; }
    internal long ColumnsBytes { get; }

    internal static SampleChunkEncodingPlan Create(ReadOnlySpan<SampleRecord> records,
        SampleChunkWork budget, int textCancellationCheckIntervalCodeUnits)
    {
        const int IndexInitialValue = 0;

        if (records.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, Empty);
        }
        if (records.Length > SampleChunkWire.MaximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessRecords);
        }

        var builder = new SampleChunkEncodingPlanBuilder(records.Length);
        for (var index = IndexInitialValue; index < records.Length; index++)
        {
            budget.Check();
            builder.Add(records[index], index, budget, textCancellationCheckIntervalCodeUnits);
        }
        return builder.Complete(budget);
    }
}
