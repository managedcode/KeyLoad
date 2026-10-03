namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkCodec
{
    internal const int MaximumEncodedBytes = SampleChunkWire.MaximumEncodedBytes;

    internal static byte[] Encode(ReadOnlySpan<SampleRecord> records, ReadExecutionBudget budget,
        int maximumBytes = MaximumEncodedBytes)
    {
        ArgumentNullException.ThrowIfNull(budget);
        SampleChunkWire.ValidateMaximum(maximumBytes);
        budget.Check();
        var plan = SampleChunkEncodingPlan.Create(records, budget);
        if (plan.ColumnsBytes > maximumBytes - SampleChunkWire.ChecksumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
        var unsealed = SampleChunkColumnEncoder.CreatePayload(records, plan, budget);
        var payload = unsealed with { Checksum = SampleChunkChecksum.Compute(unsealed, budget) };
        budget.Check();
        var measured = NativeSerialization.Measure(payload);
        budget.Check();
        if (measured > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
        var encoded = NativeSerialization.Serialize(payload);
        budget.Check();
        if (encoded.Length > maximumBytes || encoded.Length != measured)
        {
            throw Errors.Fail(encoded.Length > maximumBytes ? ErrorCode.BudgetExceeded : ErrorCode.Corruption,
                encoded.Length > maximumBytes ? SampleChunkWire.ExcessBytes : SampleChunkWire.InvalidShape);
        }
        return encoded;
    }

    internal static SampleRecord[] Decode(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget,
        int maximumBytes = MaximumEncodedBytes)
    {
        ArgumentNullException.ThrowIfNull(budget);
        SampleChunkWire.ValidateMaximum(maximumBytes);
        if (bytes.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
        budget.Check();
        var payload = NativeSerialization.Deserialize<SampleChunkPayload>(bytes);
        budget.Check();
        if (payload.FormatVersion != SampleChunkWire.CurrentVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, SampleChunkWire.UnsupportedVersion);
        }
        ValidatePayloadShape(payload);
        SampleChunkChecksum.Verify(payload, budget);
        SampleChunkNumericValidator.Validate(payload, budget);
        SampleChunkTextValidator.Validate(payload, budget);
        var text = SampleChunkTextDecoder.Decode(payload, budget);
        var records = SampleChunkRecordDecoder.Decode(payload, text, budget);
        budget.Check();
        return records;
    }

    private static void ValidatePayloadShape(SampleChunkPayload payload)
    {
        if (payload.RecordCount > SampleChunkWire.MaximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessRecords);
        }
        SampleChunkWire.Require(payload.RecordCount > 0 && payload.UtcTicks.Length > 0
            && payload.Offsets.Length > 0 && payload.Sequences.Length > 0 && payload.Values.Length > 0
            && payload.Series.Length > 0 && payload.EventIds.Length > 0 && payload.Tags.Length > 0);
        var columns = (long)payload.UtcTicks.Length + payload.Offsets.Length + payload.Sequences.Length
            + payload.Values.Length + payload.Series.Length + payload.EventIds.Length + payload.Tags.Length;
        SampleChunkWire.Require(columns <= MaximumEncodedBytes - SampleChunkWire.ChecksumBytes);
    }
}
