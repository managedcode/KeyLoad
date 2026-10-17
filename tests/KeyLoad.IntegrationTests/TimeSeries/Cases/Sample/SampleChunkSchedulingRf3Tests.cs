using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[NotInParallel]
internal sealed class SampleChunkSchedulingRf3Tests
{
    [Test]
    public async Task AcChunk009012ActualProviderReturnedBeforeAdmissionAcknowledgementJoinsColdAndOriginalMerge()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(run.Token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(run.Token).ConfigureAwait(false);
        await SampleChunkSchedulingRf3Flow.ExecuteAsync(wave, run.Token).ConfigureAwait(false);
    }
    [Test]
    public async Task AcChunk009012ActualThirtyTwoChargedAdmissionsColdRefusalManualSettlementThenFreshHealthy()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(run.Token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(run.Token).ConfigureAwait(false);
        await SampleChunkPendingRf3Flow.ExecuteAsync(wave, run.Token).ConfigureAwait(false);
    }
    [Test]
    public async Task AcChunk009012OriginalReturnedProviderAllSixSigkillNativeJobIdentityColdAndFreshHealthy()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(run.Token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(run.Token).ConfigureAwait(false);
        await SampleChunkSchedulingRf3Flow.ExecuteAsync(wave, run.Token, abrupt: true).ConfigureAwait(false);
    }
}
