using KeyLoad.Core.TimeSeries;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkMultiblockBoundaryRefusal
{
    internal static async Task RunAsync(ChunkSelectedIntegrityFixture fixture, bool unknownVersion)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Owner.ReadSampleChunkWindow(
            SampleChunkCanonicalFixture.Principal, new(fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, fixture.WindowId, null, null, ChunkMultiblockBoundaryData.HealthyCount)));
        await Assert.That(failure.Code).IsEqualTo(unknownVersion ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(unknownVersion ? SampleChunkWire.UnsupportedVersion : SampleChunkLifecycleProtocol.Corrupt);
        await Assert.That(failure.InnerException).IsNull();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }
}
