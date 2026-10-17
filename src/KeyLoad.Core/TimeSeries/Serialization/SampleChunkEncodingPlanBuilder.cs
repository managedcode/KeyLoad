namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleChunkEncodingPlanBuilder
{
    private const string SeriesMismatch = "A sample chunk must contain one series identity.";
    private const string DuplicateEvent = "A sample chunk cannot repeat an event identity.";
    private const string InvalidSequence = "Sample sequences must be positive.";
    private const string InvalidValue = "Sample values must be finite.";
    private readonly Dictionary<string, int> tagIndexes;
    private readonly List<string> tagDictionary;
    private readonly HashSet<string> eventIds;
    private long ticksBytes;
    private long sequencesBytes;
    private long valuesBytes;
    private long seriesBytes;
    private long eventIdsBytes;
    private long tagTextAndIndexesBytes;
    private long previousTicks;
    private long previousSequence;
    private ulong previousValueBits;
    private string? seriesId;
    private int count;

    internal SampleChunkEncodingPlanBuilder(int capacity)
    {
        tagIndexes = new(capacity, StringComparer.Ordinal);
        tagDictionary = new(capacity);
        eventIds = new(capacity, StringComparer.Ordinal);
    }

    internal void Add(SampleRecord? record, int index, SampleChunkWork budget, int textCancellationCheckIntervalCodeUnits)
    {
        const int EmptyIndex = 0;
        const int RecordCountSingleItemCount = 1;

        var ownedRecord = record ?? throw Errors.Fail(ErrorCode.Validation, SampleChunkWire.InvalidContent);
        ValidateRecord(ownedRecord);
        var sample = ownedRecord.Sample;
        seriesId ??= ownedRecord.SeriesId;
        if (index == EmptyIndex)
        {
            seriesBytes = SampleChunkText.FramedSize(seriesId, budget, textCancellationCheckIntervalCodeUnits);
        }
        if (!string.Equals(seriesId, ownedRecord.SeriesId, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, SeriesMismatch);
        }
        if (!eventIds.Add(sample.EventId))
        {
            throw Errors.Fail(ErrorCode.Validation, DuplicateEvent);
        }
        ValidateOrdering(ownedRecord, index);
        AddColumnLengths(ownedRecord, index, budget, textCancellationCheckIntervalCodeUnits);
        CheckCumulativeSize(index + RecordCountSingleItemCount);
        previousTicks = sample.Timestamp.UtcTicks;
        previousSequence = ownedRecord.Sequence;
        previousValueBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(sample.Value));
        count++;
    }

    internal SampleChunkEncodingPlan Complete(SampleChunkWork budget)
    {
        const long TotalInitialValue = 0L;

        budget.Check();
        var offsetsBytes = checked(count * sizeof(short));
        var tagCountBytes = SampleChunkWire.VarUIntLength((ulong)tagDictionary.Count);
        var total = TotalInitialValue;
        SampleChunkWire.AddSize(ref total, ticksBytes);
        SampleChunkWire.AddSize(ref total, offsetsBytes);
        SampleChunkWire.AddSize(ref total, sequencesBytes);
        SampleChunkWire.AddSize(ref total, valuesBytes);
        SampleChunkWire.AddSize(ref total, seriesBytes);
        SampleChunkWire.AddSize(ref total, eventIdsBytes);
        SampleChunkWire.AddSize(ref total, tagCountBytes);
        SampleChunkWire.AddSize(ref total, tagTextAndIndexesBytes);
        return new(tagIndexes, [.. tagDictionary], checked((int)ticksBytes), offsetsBytes,
            checked((int)sequencesBytes), checked((int)valuesBytes), checked((int)seriesBytes),
            checked((int)eventIdsBytes), checked((int)(tagTextAndIndexesBytes + tagCountBytes)), total);
    }

    private static void ValidateRecord(SampleRecord? record)
    {
        if (record is null || record.Sample is null || string.IsNullOrEmpty(record.SeriesId)
            || string.IsNullOrEmpty(record.Sample.EventId) || string.IsNullOrEmpty(record.TagsJson))
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkWire.InvalidContent);
        }
        if (record.SeriesId.Length > SampleChunkWire.MaximumEncodedBytes
            || record.Sample.EventId.Length > SampleChunkWire.MaximumEncodedBytes
            || record.TagsJson.Length > SampleChunkWire.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
    }

    private void ValidateOrdering(SampleRecord record, int index)
    {
        const int IndexValidationBoundary = 0;
        const int SequenceValidationBoundary = 0;

        var ticks = record.Sample.Timestamp.UtcTicks;
        if (index > IndexValidationBoundary && (ticks < previousTicks
            || ticks == previousTicks && record.Sequence <= previousSequence))
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkWire.InvalidContent);
        }
        if (record.Sequence <= SequenceValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSequence);
        }
        if (!double.IsFinite(record.Sample.Value))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidValue);
        }
    }

    private void AddColumnLengths(SampleRecord record, int index, SampleChunkWork budget, int textCancellationCheckIntervalCodeUnits)
    {
        const int EmptyIndex = 0;
        const int FirstValueRecordIndex = 0;

        var ticks = record.Sample.Timestamp.UtcTicks;
        var tickValue = index == EmptyIndex ? (ulong)ticks : (ulong)(ticks - previousTicks);
        var sequenceValue = index == EmptyIndex ? (ulong)record.Sequence
            : SampleChunkWire.ZigZag(record.Sequence - previousSequence);
        var valueBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(record.Sample.Value));
        ticksBytes += SampleChunkWire.VarUIntLength(tickValue);
        sequencesBytes += SampleChunkWire.VarUIntLength(sequenceValue);
        valuesBytes += SampleChunkWire.VarUIntLength(index == FirstValueRecordIndex ? valueBits : valueBits ^ previousValueBits);
        eventIdsBytes += SampleChunkText.FramedSize(record.Sample.EventId, budget, textCancellationCheckIntervalCodeUnits);
        if (!tagIndexes.TryGetValue(record.TagsJson, out var tagIndex))
        {
            tagIndex = tagDictionary.Count;
            tagIndexes.Add(record.TagsJson, tagIndex);
            tagDictionary.Add(record.TagsJson);
            tagTextAndIndexesBytes += SampleChunkText.FramedSize(record.TagsJson, budget, textCancellationCheckIntervalCodeUnits);
        }
        tagTextAndIndexesBytes += SampleChunkWire.VarUIntLength((ulong)tagIndex);
    }

    private void CheckCumulativeSize(int recordCount)
    {
        const long TotalInitialValue = 0L;

        var total = TotalInitialValue;
        SampleChunkWire.AddSize(ref total, ticksBytes);
        SampleChunkWire.AddSize(ref total, checked(recordCount * sizeof(short)));
        SampleChunkWire.AddSize(ref total, sequencesBytes);
        SampleChunkWire.AddSize(ref total, valuesBytes);
        SampleChunkWire.AddSize(ref total, seriesBytes);
        SampleChunkWire.AddSize(ref total, eventIdsBytes);
        SampleChunkWire.AddSize(ref total, SampleChunkWire.VarUIntLength((ulong)tagDictionary.Count));
        SampleChunkWire.AddSize(ref total, tagTextAndIndexesBytes);
    }
}
