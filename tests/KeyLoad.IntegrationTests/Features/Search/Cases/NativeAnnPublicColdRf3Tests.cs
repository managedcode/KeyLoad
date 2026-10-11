using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class NativeAnnPublicColdRf3Tests
{
    [Test]
    public async Task DedicatedSdkOfficialMcpAndQ1NativeGenerationRetainsOriginalReceiptAcrossTwoColdCutsThenFencedReleaseAndHealthyConsumer()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits(),
                new QueryExecutionOptions { EnableApproximateSearch = true });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<NativeAnnPublicColdRf3Tests>(nameof(
                    DedicatedSdkOfficialMcpAndQ1NativeGenerationRetainsOriginalReceiptAcrossTwoColdCutsThenFencedReleaseAndHealthyConsumer));
                await NativeAnnPublicColdFlow.RunAsync(fixture);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
