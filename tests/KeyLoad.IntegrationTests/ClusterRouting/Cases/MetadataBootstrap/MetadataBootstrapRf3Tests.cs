using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class MetadataBootstrapRf3Tests
{
    [Test]
    public async Task FreshBootstrapCatalogCasSurvivesActualLeaderLossPersistedPolicyRepairSdkMcpReplayAndTwoCold()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        ClusterFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                fixture = new ClusterFixture();
                await ServerFailureObserver.ObserveAsync(() => InitializeAndRunAsync(fixture, joined.Token), failures);
            }
            finally
            {
                if (fixture is not null)
                {
                    await fixture.DisposeAsync();
                }
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task InitializeAndRunAsync(ClusterFixture fixture, CancellationToken token)
    {
        await fixture.InitializeAsync();
        fixture.RegisterNativeCoverageCase<MetadataBootstrapRf3Tests>(
            nameof(FreshBootstrapCatalogCasSurvivesActualLeaderLossPersistedPolicyRepairSdkMcpReplayAndTwoCold));
        await MetadataBootstrapRf3Flow.RunAsync(fixture, token);
    }

}
