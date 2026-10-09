using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class NativeAnnWaitRf3Tests
{
    [Test]
    public async Task RealSdkOfficialMcpAndBothQ1MinimumWaitRejectDeniedStaleThenRestoreDeletedExclusionAndColdHealthy()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits(),
                new QueryExecutionOptions { EnableApproximateSearch = true });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<NativeAnnWaitRf3Tests>(
                    nameof(RealSdkOfficialMcpAndBothQ1MinimumWaitRejectDeniedStaleThenRestoreDeletedExclusionAndColdHealthy));
                await NativeAnnWaitRf3Scenario.RunAsync(fixture, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
