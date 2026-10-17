using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[NotInParallel]
internal sealed class SampleChunkExplicitMinimumRf3Tests
{
    [Test]
    public async Task ExplicitMinimumCrossVoterAuthorityRefusalsRepairMovementReceiptsAndTwoColdOwners()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await ChunkMinimumRf3Owned.RunAsync(caller.Token);
    }
}
