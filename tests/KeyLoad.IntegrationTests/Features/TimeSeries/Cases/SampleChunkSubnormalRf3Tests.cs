using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkRf3Protocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleChunkSubnormalRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcSeriesPublicSubnormal001004SealMergeReplayAndTwoColdKeepExactPublicSamples()
    {
        fixture.RegisterNativeCoverageCase<SampleChunkSubnormalRf3Tests>(nameof(
            AcSeriesPublicSubnormal001004SealMergeReplayAndTwoColdKeepExactPublicSamples));
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                deadline = McpCallerDeadline.Create();
                await SampleChunkSubnormalRf3Flow.RunAsync(fixture, deadline.Token);
            }, failures);
        }
        finally
        {
            if (deadline is not null)
            { ServerFailureObserver.Observe(deadline.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
