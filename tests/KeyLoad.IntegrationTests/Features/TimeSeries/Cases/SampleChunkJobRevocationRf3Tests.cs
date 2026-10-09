using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[NotInParallel]
internal sealed class SampleChunkJobRevocationRf3Tests
{
    [Test]
    public async Task AcChunk009012HeldNativeBackgroundCreatorRevocationThenFreshEpochMergeAndColdReplay()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(run.Token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(run.Token).ConfigureAwait(false);
        await SampleChunkJobRevocationFlow.ExecuteAsync(wave, run.Token).ConfigureAwait(false);
    }
}
