namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkRecordDecoder
{
    internal static SampleRecord[] Decode(SampleChunkPayload payload, SampleChunkDecodedText text,
        ReadExecutionBudget budget)
    {
        var result = new SampleRecord[payload.RecordCount];
        var ticks = new SampleChunkReader(payload.UtcTicks.Span);
        var offsets = new SampleChunkReader(payload.Offsets.Span);
        var sequences = new SampleChunkReader(payload.Sequences.Span);
        var values = new SampleChunkReader(payload.Values.Span);
        long previousTicks = 0;
        long previousSequence = 0;
        ulong previousValue = 0;
        for (var index = 0; index < result.Length; index++)
        {
            budget.Check();
            var currentTicks = SampleChunkNumericValidator.DecodeTicks(ticks.ReadVarUInt(), index, previousTicks);
            var offset = TimeSpan.FromMinutes(offsets.ReadInt16LittleEndian());
            var currentSequence = SampleChunkNumericValidator.DecodeSequence(sequences.ReadVarUInt(), index,
                previousSequence);
            var valuePart = values.ReadVarUInt();
            var currentValue = index == 0 ? valuePart : valuePart ^ previousValue;
            var utcTicks = new DateTimeOffset(currentTicks, TimeSpan.Zero);
            var timestamp = utcTicks.ToOffset(offset);
            var eventId = text.EventIds[index];
            var tags = text.TagDictionary[text.TagIndexes[index]];
            result[index] = new(text.SeriesId, new(eventId, timestamp,
                BitConverter.Int64BitsToDouble(unchecked((long)currentValue))), currentSequence, tags);
            previousTicks = currentTicks;
            previousSequence = currentSequence;
            previousValue = currentValue;
        }
        ticks.RequireEnd();
        offsets.RequireEnd();
        sequences.RequireEnd();
        values.RequireEnd();
        budget.Check();
        return result;
    }
}
