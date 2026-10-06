using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class SampleChunkBenchmarkState
{
    private readonly SampleRecord[] records;
    private readonly byte[][] nativeValues;
    private readonly byte[] chunk;
    private readonly IOptions<DatabaseLimits> limitsOptions;
    private readonly IOptions<TimeSeriesExecutionOptions> executionOptions;

    internal SampleChunkBenchmarkState(SampleRecord[] records, IOptions<DatabaseLimits> limitsOptions,
        IOptions<TimeSeriesExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.records = records;
        this.limitsOptions = limitsOptions;
        this.executionOptions = executionOptions;
        nativeValues = NativeEncode();
        chunk = ChunkEncode();
        RequireExact(records, NativeDecode());
        RequireExact(records, ChunkDecode());
    }

    internal long NativeBytes => nativeValues.Sum(value => (long)value.Length);
    internal int ChunkBytes => chunk.Length;
    internal int RecordCount => records.Length;
    internal IReadOnlyList<byte[]> NativeValues => nativeValues;
    internal ReadOnlySpan<byte> ChunkValue => chunk;

    internal byte[][] NativeEncode()
    {
        const int IndexInitialValue = 0;

        var budget = Budget();
        var values = new byte[records.Length][];
        for (var index = IndexInitialValue; index < records.Length; index++)
        {
            budget.Check();
            values[index] = NativeSerialization.Serialize(records[index]);
            budget.Check();
        }
        return values;
    }

    internal byte[] ChunkEncode() => SampleChunkCodec.Encode(records, Budget(), executionOptions);

    internal SampleRecord[] NativeDecode()
    {
        const int IndexInitialValue = 0;

        var budget = Budget();
        var decoded = new SampleRecord[records.Length];
        for (var index = IndexInitialValue; index < records.Length; index++)
        {
            budget.ChargeBytes(nativeValues[index].Length);
            decoded[index] = NativeSerialization.Deserialize<SampleRecord>(nativeValues[index]);
            budget.Check();
        }
        return decoded;
    }

    internal SampleRecord[] ChunkDecode()
    {
        var budget = Budget();
        budget.ChargeBytes(chunk.Length);
        return SampleChunkCodec.Decode(chunk, budget, executionOptions);
    }

    private ReadExecutionBudget Budget() => new(limitsOptions);

    private static void RequireExact(SampleRecord[] expected, SampleRecord[] actual)
    {
        const string RequireExactFailureMessage = "The sample chunk benchmark corpus failed exact roundtrip verification.";
        const int IndexInitialValue = 0;

        if (actual.Length != expected.Length)
        {
            throw new InvalidOperationException(RequireExactFailureMessage);
        }
        for (var index = IndexInitialValue; index < expected.Length; index++)
        {
            var left = expected[index];
            var right = actual[index];
            if (left.SeriesId != right.SeriesId || left.Sequence != right.Sequence || left.TagsJson != right.TagsJson
                || left.Sample.EventId != right.Sample.EventId
                || left.Sample.Timestamp.Ticks != right.Sample.Timestamp.Ticks
                || left.Sample.Timestamp.Offset != right.Sample.Timestamp.Offset
                || BitConverter.DoubleToInt64Bits(left.Sample.Value) != BitConverter.DoubleToInt64Bits(right.Sample.Value))
            {
                throw new InvalidOperationException(RequireExactFailureMessage);
            }
        }
    }
}
