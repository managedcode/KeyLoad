using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkClusterProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleChunkSubnormalClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSeriesPublicSubnormal001004SealMergeReplayAndTwoColdKeepExactPublicSamples()
    {
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                deadline = McpCallerDeadline.Create();
                await SampleChunkSubnormalClusterFlow.RunAsync(fixture, deadline.Token);
            }, failures);
        }
        finally
        {
            if (deadline is not null)
            {
                ServerFailureObserver.Observe(deadline.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
