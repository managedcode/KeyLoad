using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class AnnPublicRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualOrdinarySdkOfficialMcpAndBothCallReadsUseProvisionedOneCutAndTruthfulEmptyPage(bool empty)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits(),
                new QueryExecutionOptions { EnableApproximateSearch = true });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<AnnPublicRf3Tests>(
                    nameof(ActualOrdinarySdkOfficialMcpAndBothCallReadsUseProvisionedOneCutAndTruthfulEmptyPage));
                await AnnPublicRf3Scenario.RunAsync(fixture, empty, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
