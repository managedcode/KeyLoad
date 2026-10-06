namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkNumericValidator
{
    internal static void Validate(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        const int PriorTicksInitialValue = 0;
        const int PriorSequenceInitialValue = 0;
        const int PriorOrderedSequenceInitialValue = 0;
        const int PriorValueInitialValue = 0;
        const int IndexInitialValue = 0;
        const int IndexValidationBoundary = 0;
        const int EmptyIndex = 0;

        var count = payload.RecordCount;
        SampleChunkWire.Require(payload.UtcTicks.Length >= count
            && payload.Offsets.Length == count * sizeof(short)
            && payload.Sequences.Length >= count && payload.Values.Length >= count);
        var ticks = new SampleChunkReader(payload.UtcTicks.Span);
        var offsets = new SampleChunkReader(payload.Offsets.Span);
        var sequences = new SampleChunkReader(payload.Sequences.Span);
        var values = new SampleChunkReader(payload.Values.Span);
        long priorTicks = PriorTicksInitialValue;
        long priorSequence = PriorSequenceInitialValue;
        long priorOrderedSequence = PriorOrderedSequenceInitialValue;
        ulong priorValue = PriorValueInitialValue;
        for (var index = IndexInitialValue; index < count; index++)
        {
            budget.Check();
            var tickPart = ticks.ReadVarUInt();
            var currentTicks = DecodeTicks(tickPart, index, priorTicks);
            var offsetMinutes = offsets.ReadInt16LittleEndian();
            ValidateOffset(currentTicks, offsetMinutes);
            var currentSequence = DecodeSequence(sequences.ReadVarUInt(), index, priorSequence);
            if (index > IndexValidationBoundary && currentTicks == priorTicks && currentSequence <= priorOrderedSequence)
            {
                SampleChunkWire.Require(false);
            }
            var valuePart = values.ReadVarUInt();
            var valueBits = index == EmptyIndex ? valuePart : valuePart ^ priorValue;
            SampleChunkWire.Require(double.IsFinite(BitConverter.Int64BitsToDouble(unchecked((long)valueBits))));
            priorTicks = currentTicks;
            priorSequence = currentSequence;
            priorOrderedSequence = currentSequence;
            priorValue = valueBits;
        }
        ticks.RequireEnd();
        offsets.RequireEnd();
        sequences.RequireEnd();
        values.RequireEnd();
        budget.Check();
    }

    internal static long DecodeTicks(ulong encoded, int index, long previous)
    {
        const int EmptyIndex = 0;

        var maximum = (ulong)DateTimeOffset.MaxValue.UtcTicks;
        if (index == EmptyIndex)
        {
            SampleChunkWire.Require(encoded <= maximum);
            return (long)encoded;
        }
        SampleChunkWire.Require(encoded <= maximum - (ulong)previous);
        return previous + (long)encoded;
    }

    internal static long DecodeSequence(ulong encoded, int index, long previous)
    {
        const int EmptyIndex = 0;
        const int EmptyEncodedSequence = 0;
        const int DeltaValidationBoundary = 0;
        const int FirstSequence = 1;

        if (index == EmptyIndex)
        {
            SampleChunkWire.Require(encoded is > EmptyEncodedSequence and <= long.MaxValue);
            return (long)encoded;
        }
        var delta = SampleChunkWire.UnZigZag(encoded);
        if (delta > DeltaValidationBoundary)
        {
            SampleChunkWire.Require(previous <= long.MaxValue - delta);
        }
        else
        {
            SampleChunkWire.Require(delta >= FirstSequence - previous);
        }
        return previous + delta;
    }

    private static void ValidateOffset(long utcTicks, short offsetMinutes)
    {
        const int MinimumNativeUtcOffsetMinutes = -840;
        const int MaximumNativeUtcOffsetMinutes = 840;

        SampleChunkWire.Require(offsetMinutes is >= MinimumNativeUtcOffsetMinutes and <= MaximumNativeUtcOffsetMinutes);
        var localTicks = utcTicks + (long)offsetMinutes * TimeSpan.TicksPerMinute;
        SampleChunkWire.Require(localTicks >= DateTimeOffset.MinValue.Ticks
            && localTicks <= DateTimeOffset.MaxValue.Ticks);
    }
}
