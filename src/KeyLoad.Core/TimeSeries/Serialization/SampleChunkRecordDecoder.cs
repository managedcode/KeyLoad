namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkRecordDecoder
{
    internal static SampleRecord[] Decode(SampleChunkPayload payload, SampleChunkDecodedText text,
        SampleChunkWork budget)
    {
        const int PreviousTicksInitialValue = 0;
        const int PreviousSequenceInitialValue = 0;
        const int PreviousValueInitialValue = 0;
        const int IndexInitialValue = 0;
        const int EmptyIndex = 0;

        var result = new SampleRecord[payload.RecordCount];
        var ticks = new SampleChunkReader(payload.UtcTicks.Span);
        var offsets = new SampleChunkReader(payload.Offsets.Span);
        var sequences = new SampleChunkReader(payload.Sequences.Span);
        var values = new SampleChunkReader(payload.Values.Span);
        long previousTicks = PreviousTicksInitialValue;
        long previousSequence = PreviousSequenceInitialValue;
        ulong previousValue = PreviousValueInitialValue;
        for (var index = IndexInitialValue; index < result.Length; index++)
        {
            budget.Check();
            var currentTicks = SampleChunkNumericValidator.DecodeTicks(ticks.ReadVarUInt(), index, previousTicks);
            var offset = TimeSpan.FromMinutes(offsets.ReadInt16LittleEndian());
            var currentSequence = SampleChunkNumericValidator.DecodeSequence(sequences.ReadVarUInt(), index,
                previousSequence);
            var valuePart = values.ReadVarUInt();
            var currentValue = index == EmptyIndex ? valuePart : valuePart ^ previousValue;
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
