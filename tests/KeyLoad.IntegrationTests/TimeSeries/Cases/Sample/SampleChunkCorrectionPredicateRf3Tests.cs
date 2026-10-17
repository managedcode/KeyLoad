using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[NotInParallel]
internal sealed class SampleChunkCorrectionPredicateRf3Tests
{
    [Test]
    public async Task CorrectionOnlyTagHitMissAndWholeLimitRefusalSurviveNativeMergeReplayAndTwoColdOwners()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await SampleChunkCorrectionPredicateRf3Flow.RunAsync(caller.Token);
    }
}
