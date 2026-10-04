using System.Buffers.Binary;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkColumnEncoder
{
    internal static SampleChunkPayload CreatePayload(ReadOnlySpan<SampleRecord> records,
        SampleChunkEncodingPlan plan, ReadExecutionBudget budget)
    {
        var utcTicks = new byte[plan.UtcTicksBytes];
        var offsets = new byte[plan.OffsetsBytes];
        var sequences = new byte[plan.SequencesBytes];
        var values = new byte[plan.ValuesBytes];
        var series = new byte[plan.SeriesBytes];
        var eventIds = new byte[plan.EventIdsBytes];
        var tags = new byte[plan.TagsBytes];
        WriteNumericColumns(records, utcTicks, offsets, sequences, values, budget);
        WriteTextColumns(records, plan, series, eventIds, tags, budget);
        SampleChunkWire.Require(utcTicks.Length > 0 && offsets.Length > 0 && sequences.Length > 0
            && values.Length > 0 && series.Length > 0 && eventIds.Length > 0 && tags.Length > 0);
        return new(SampleChunkWire.CurrentVersion, records.Length, utcTicks, offsets, sequences,
            values, series, eventIds, tags, ReadOnlyMemory<byte>.Empty);
    }

    private static void WriteNumericColumns(ReadOnlySpan<SampleRecord> records, byte[] utcTicks,
        byte[] offsets, byte[] sequences, byte[] values, ReadExecutionBudget budget)
    {
        var tickPosition = 0;
        var offsetPosition = 0;
        var sequencePosition = 0;
        var valuePosition = 0;
        long priorTicks = 0;
        long priorSequence = 0;
        ulong priorValue = 0;
        for (var index = 0; index < records.Length; index++)
        {
            budget.Check();
            var record = records[index];
            var ticks = record.Sample.Timestamp.UtcTicks;
            var tickDelta = index == 0 ? (ulong)ticks : (ulong)(ticks - priorTicks);
            SampleChunkWire.WriteVarUInt(utcTicks, ref tickPosition, tickDelta);
            var offsetMinutes = checked((short)(record.Sample.Timestamp.Offset.Ticks / TimeSpan.TicksPerMinute));
            BinaryPrimitives.WriteInt16LittleEndian(offsets.AsSpan(offsetPosition), offsetMinutes);
            offsetPosition += sizeof(short);
            var sequenceDelta = index == 0 ? (ulong)record.Sequence
                : SampleChunkWire.ZigZag(record.Sequence - priorSequence);
            SampleChunkWire.WriteVarUInt(sequences, ref sequencePosition, sequenceDelta);
            var valueBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(record.Sample.Value));
            SampleChunkWire.WriteVarUInt(values, ref valuePosition,
                index == 0 ? valueBits : valueBits ^ priorValue);
            priorTicks = ticks;
            priorSequence = record.Sequence;
            priorValue = valueBits;
        }
        SampleChunkWire.Require(tickPosition == utcTicks.Length && offsetPosition == offsets.Length
            && sequencePosition == sequences.Length && valuePosition == values.Length);
    }

    private static void WriteTextColumns(ReadOnlySpan<SampleRecord> records, SampleChunkEncodingPlan plan,
        byte[] series, byte[] eventIds, byte[] tags, ReadExecutionBudget budget)
    {
        var seriesPosition = 0;
        var eventPosition = 0;
        var tagPosition = 0;
        SampleChunkText.WriteFramed(records[0].SeriesId, series, ref seriesPosition, budget);
        for (var index = 0; index < records.Length; index++)
        {
            budget.Check();
            SampleChunkText.WriteFramed(records[index].Sample.EventId, eventIds, ref eventPosition, budget);
        }
        SampleChunkWire.WriteVarUInt(tags, ref tagPosition, (ulong)plan.TagDictionary.Length);
        foreach (var text in plan.TagDictionary)
        {
            budget.Check();
            SampleChunkText.WriteFramed(text, tags, ref tagPosition, budget);
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
