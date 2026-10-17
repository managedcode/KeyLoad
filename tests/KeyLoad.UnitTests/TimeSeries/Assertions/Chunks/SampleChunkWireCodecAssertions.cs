using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkWireCodecAssertions
{
    internal static byte[] Encoded(SampleRecord[] records)
        => SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());

    internal static KeyLoadException DecodeFailure(byte[] bytes)
        => Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(bytes,
            SampleChunkTestData.ChargedDecodeBudget(bytes), UnitExecutionOptions.TimeSeriesExecution()));
}
