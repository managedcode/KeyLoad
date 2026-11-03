using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryRefusal
{
    internal static async Task RunAsync(ChunkSelectedIntegrityFixture fixture)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Owner.ReadSampleChunkWindow(
            SampleChunkCanonicalFixture.Principal, new(fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, fixture.WindowId, null, null, ChunkMultiblockBoundaryData.HealthyCount)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(SampleChunkLifecycleProtocol.Corrupt);
        await Assert.That(failure.InnerException).IsNull();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }
}
