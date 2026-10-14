using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkCodecRoundTripAssertions
{

    internal static async Task RoundTripExact(SampleRecord[] expected)
    {
        var encoded = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var actual = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution());
        await SampleChunkTestData.AssertRecordsExact(expected, actual);
    }

    internal static async Task AssertRawFoldSame(SampleRecord[] expected, SampleRecord[] actual)
    {
        var expectedFold = RawFold(expected);
        var actualFold = RawFold(actual);
        await Assert.That(actualFold.Count).IsEqualTo(expectedFold.Count);
        await Assert.That(BitConverter.DoubleToInt64Bits(actualFold.Sum))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(expectedFold.Sum));
        await Assert.That(actualFold.Minimum).IsEqualTo(expectedFold.Minimum);
        await Assert.That(actualFold.Maximum).IsEqualTo(expectedFold.Maximum);
    }

    internal static (long Count, double Sum, double? Minimum, double? Maximum) RawFold(SampleRecord[] records)
    {
        long count = 0;
        var sum = 0d;
        double? minimum = null;
        double? maximum = null;
        foreach (var record in records)
        {
            count++;
            sum += record.Sample.Value;
            minimum = minimum is null ? record.Sample.Value : Math.Min(minimum.Value, record.Sample.Value);
            maximum = maximum is null ? record.Sample.Value : Math.Max(maximum.Value, record.Sample.Value);
        }
        return (count, sum, minimum, maximum);
    }
}
