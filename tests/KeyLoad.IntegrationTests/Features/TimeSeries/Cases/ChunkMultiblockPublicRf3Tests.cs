using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkRetentionRf3Protocol.FixtureKey)]
[NotInParallel]
internal sealed class ChunkMultiblockPublicRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task IndependentlyAuthored257RowsCrossNativeBlocksThenPagedExpiryPolicyRepairReplayAndTwoCold()
    {
        fixture.RegisterNativeCoverageCase<ChunkMultiblockPublicRf3Tests>(nameof(
            IndependentlyAuthored257RowsCrossNativeBlocksThenPagedExpiryPolicyRepairReplayAndTwoCold));
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        try
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                deadline = McpCallerDeadline.Create();
                await ChunkMultiblockRf3Flow.RunAsync(fixture, deadline.Token);
            }, failures);
        }
        finally
        {
            if (deadline is not null)
            { KeyLoad.Server.ServerFailureObserver.Observe(deadline.Dispose, failures); }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
