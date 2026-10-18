using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkRetentionClusterProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ChunkMultiblockPublicClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task IndependentlyAuthored257RowsCrossNativeBlocksThenPagedExpiryPolicyRepairReplayAndTwoCold()
    {
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        try
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                deadline = McpCallerDeadline.Create();
                await ChunkMultiblockClusterFlow.RunAsync(fixture, deadline.Token);
            }, failures);
        }
        finally
        {
            if (deadline is not null)
            {
                KeyLoad.Server.ServerFailureObserver.Observe(deadline.Dispose, failures);
            }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
