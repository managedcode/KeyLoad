using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleChunkOrderedCodecTests
{
    [Test]
    public async Task AcChunk001010OrderedApplyAndObservedReaderPreserveOriginalWireAndAllValueBits()
    {
        var rows = SampleChunkTestData.FixedRecords();
        var options = UnitExecutionOptions.TimeSeriesExecution();
        var observed = SampleChunkCodec.Encode(rows, SampleChunkTestData.Budget(), options);
        var ordered = SampleChunkCodec.EncodeOrdered(rows, options, SampleChunkCodec.MaximumEncodedBytes);
        await Assert.That(Convert.ToHexString(ordered)).IsEqualTo(Convert.ToHexString(observed));
        var decodedOrdered = SampleChunkCodec.DecodeOrdered(observed, options, SampleChunkCodec.MaximumEncodedBytes);
        var decodedObserved = SampleChunkCodec.Decode(ordered, SampleChunkTestData.Budget(), options);
        await SampleChunkTestData.AssertRecordsExact(rows, decodedOrdered);
        await SampleChunkTestData.AssertRecordsExact(rows, decodedObserved);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failed = Assert.ThrowsExactly<OperationCanceledException>(() => SampleChunkCodec.Decode(
            ordered, SampleChunkTestData.Budget(cancellationToken: cancellation.Token), options));
        await Assert.That(failed.CancellationToken).IsEqualTo(cancellation.Token);
        var healthy = SampleChunkCodec.Decode(ordered, SampleChunkTestData.Budget(), options);
        await SampleChunkTestData.AssertRecordsExact(rows, healthy);
        await Assert.That(Convert.ToHexString(ordered)).IsEqualTo(Convert.ToHexString(observed));
    }
}
