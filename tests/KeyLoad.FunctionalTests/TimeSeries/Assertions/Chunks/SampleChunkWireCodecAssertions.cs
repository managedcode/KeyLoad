using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkWireCodecAssertions
{
    internal static byte[] Encoded(SampleRecord[] records)
        => SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());

    internal static KeyLoadException DecodeFailure(byte[] bytes)
        => Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(bytes,
            SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution()));
}
