using System.Buffers.Binary;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkColumnEncoder
{
    internal static SampleChunkPayload CreatePayload(ReadOnlySpan<SampleRecord> records,
        SampleChunkEncodingPlan plan, ReadExecutionBudget budget, int hashChunkBytes, int textCancellationCheckIntervalCodeUnits)
    {
        const int EmptyEncodedColumnBytes = 0;

        var utcTicks = new byte[plan.UtcTicksBytes];
        var offsets = new byte[plan.OffsetsBytes];
        var sequences = new byte[plan.SequencesBytes];
        var values = new byte[plan.ValuesBytes];
        var series = new byte[plan.SeriesBytes];
        var eventIds = new byte[plan.EventIdsBytes];
        var tags = new byte[plan.TagsBytes];
        WriteNumericColumns(records, utcTicks, offsets, sequences, values, budget);
        WriteTextColumns(records, plan, series, eventIds, tags, budget, hashChunkBytes, textCancellationCheckIntervalCodeUnits);
        SampleChunkWire.Require(utcTicks.Length > EmptyEncodedColumnBytes && offsets.Length > EmptyEncodedColumnBytes && sequences.Length > EmptyEncodedColumnBytes
            && values.Length > EmptyEncodedColumnBytes && series.Length > EmptyEncodedColumnBytes && eventIds.Length > EmptyEncodedColumnBytes && tags.Length > EmptyEncodedColumnBytes);
        return new(SampleChunkWire.CurrentVersion, records.Length, utcTicks, offsets, sequences,
            values, series, eventIds, tags, ReadOnlyMemory<byte>.Empty);
    }

    private static void WriteNumericColumns(ReadOnlySpan<SampleRecord> records, byte[] utcTicks,
        byte[] offsets, byte[] sequences, byte[] values, ReadExecutionBudget budget)
    {
        const int TickPositionInitialValue = 0;
        const int OffsetPositionInitialValue = 0;
        const int SequencePositionInitialValue = 0;
        const int ValuePositionInitialValue = 0;
        const int PriorTicksInitialValue = 0;
        const int PriorSequenceInitialValue = 0;
        const int PriorValueInitialValue = 0;
        const int IndexInitialValue = 0;
        const int EmptyIndex = 0;
        const int FirstNumericRecordIndex = 0;

        var tickPosition = TickPositionInitialValue;
        var offsetPosition = OffsetPositionInitialValue;
        var sequencePosition = SequencePositionInitialValue;
        var valuePosition = ValuePositionInitialValue;
        long priorTicks = PriorTicksInitialValue;
        long priorSequence = PriorSequenceInitialValue;
        ulong priorValue = PriorValueInitialValue;
        for (var index = IndexInitialValue; index < records.Length; index++)
        {
            budget.Check();
            var record = records[index];
            var ticks = record.Sample.Timestamp.UtcTicks;
            var tickDelta = index == EmptyIndex ? (ulong)ticks : (ulong)(ticks - priorTicks);
            SampleChunkWire.WriteVarUInt(utcTicks, ref tickPosition, tickDelta);
            var offsetMinutes = checked((short)(record.Sample.Timestamp.Offset.Ticks / TimeSpan.TicksPerMinute));
            BinaryPrimitives.WriteInt16LittleEndian(offsets.AsSpan(offsetPosition), offsetMinutes);
            offsetPosition += sizeof(short);
            var sequenceDelta = index == EmptyIndex ? (ulong)record.Sequence
                : SampleChunkWire.ZigZag(record.Sequence - priorSequence);
            SampleChunkWire.WriteVarUInt(sequences, ref sequencePosition, sequenceDelta);
            var valueBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(record.Sample.Value));
            SampleChunkWire.WriteVarUInt(values, ref valuePosition,
                index == FirstNumericRecordIndex ? valueBits : valueBits ^ priorValue);
            priorTicks = ticks;
            priorSequence = record.Sequence;
            priorValue = valueBits;
        }
        SampleChunkWire.Require(tickPosition == utcTicks.Length && offsetPosition == offsets.Length
            && sequencePosition == sequences.Length && valuePosition == values.Length);
    }

    private static void WriteTextColumns(ReadOnlySpan<SampleRecord> records, SampleChunkEncodingPlan plan,
        byte[] series, byte[] eventIds, byte[] tags, ReadExecutionBudget budget, int hashChunkBytes,
        int textCancellationCheckIntervalCodeUnits)
    {
        const int SeriesPositionInitialValue = 0;
        const int EventPositionInitialValue = 0;
        const int TagPositionInitialValue = 0;
        const int FirstSeriesRecordIndex = 0;
        const int IndexInitialValue = 0;

        var seriesPosition = SeriesPositionInitialValue;
        var eventPosition = EventPositionInitialValue;
        var tagPosition = TagPositionInitialValue;
        SampleChunkText.WriteFramed(records[FirstSeriesRecordIndex].SeriesId, series, ref seriesPosition, budget,
            hashChunkBytes, textCancellationCheckIntervalCodeUnits);
        for (var index = IndexInitialValue; index < records.Length; index++)
        {
            budget.Check();
            SampleChunkText.WriteFramed(records[index].Sample.EventId, eventIds, ref eventPosition, budget,
                hashChunkBytes, textCancellationCheckIntervalCodeUnits);
        }
        SampleChunkWire.WriteVarUInt(tags, ref tagPosition, (ulong)plan.TagDictionary.Length);
        foreach (var text in plan.TagDictionary)
        {
            budget.Check();
            SampleChunkText.WriteFramed(text, tags, ref tagPosition, budget, hashChunkBytes, textCancellationCheckIntervalCodeUnits);
        }
        foreach (var record in records)
        {
            budget.Check();
            SampleChunkWire.WriteVarUInt(tags, ref tagPosition, (ulong)plan.TagIndexes[record.TagsJson]);
        }
        SampleChunkWire.Require(seriesPosition == series.Length && eventPosition == eventIds.Length
            && tagPosition == tags.Length);
    }
}
