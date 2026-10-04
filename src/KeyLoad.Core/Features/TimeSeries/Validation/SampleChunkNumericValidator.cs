namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkNumericValidator
{
    internal static void Validate(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        var count = payload.RecordCount;
        SampleChunkWire.Require(payload.UtcTicks.Length >= count
            && payload.Offsets.Length == count * sizeof(short)
            && payload.Sequences.Length >= count && payload.Values.Length >= count);
        var ticks = new SampleChunkReader(payload.UtcTicks.Span);
        var offsets = new SampleChunkReader(payload.Offsets.Span);
        var sequences = new SampleChunkReader(payload.Sequences.Span);
        var values = new SampleChunkReader(payload.Values.Span);
        long priorTicks = 0;
        long priorSequence = 0;
        long priorOrderedSequence = 0;
        ulong priorValue = 0;
        for (var index = 0; index < count; index++)
        {
            budget.Check();
            var tickPart = ticks.ReadVarUInt();
            var currentTicks = DecodeTicks(tickPart, index, priorTicks);
            var offsetMinutes = offsets.ReadInt16LittleEndian();
            ValidateOffset(currentTicks, offsetMinutes);
            var currentSequence = DecodeSequence(sequences.ReadVarUInt(), index, priorSequence);
            if (index > 0 && currentTicks == priorTicks && currentSequence <= priorOrderedSequence)
            {
                SampleChunkWire.Require(false);
            }
            var valuePart = values.ReadVarUInt();
            var valueBits = index == 0 ? valuePart : valuePart ^ priorValue;
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
        var maximum = (ulong)DateTimeOffset.MaxValue.UtcTicks;
        if (index == 0)
        {
            SampleChunkWire.Require(encoded <= maximum);
            return (long)encoded;
        }
        SampleChunkWire.Require(encoded <= maximum - (ulong)previous);
        return previous + (long)encoded;
    }

    internal static long DecodeSequence(ulong encoded, int index, long previous)
    {
        if (index == 0)
        {
            SampleChunkWire.Require(encoded is > 0 and <= long.MaxValue);
            return (long)encoded;
        }
        var delta = SampleChunkWire.UnZigZag(encoded);
        if (delta > 0)
        {
            SampleChunkWire.Require(previous <= long.MaxValue - delta);
        }
        else
        {
            SampleChunkWire.Require(delta >= 1 - previous);
        }
        return previous + delta;
    }

    private static void ValidateOffset(long utcTicks, short offsetMinutes)
    {
        SampleChunkWire.Require(offsetMinutes is >= -840 and <= 840);
        var localTicks = utcTicks + (long)offsetMinutes * TimeSpan.TicksPerMinute;
        SampleChunkWire.Require(localTicks >= DateTimeOffset.MinValue.Ticks
            && localTicks <= DateTimeOffset.MaxValue.Ticks);
    }
}
