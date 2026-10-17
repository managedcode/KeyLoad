using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class NativeAnnSettledRf3Tests
{
    [Test]
    public async Task OriginalFullResultReplaysAcrossColdRf3ThenRefusesPriorParentAndChangedCurrentAuthority()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits(),
                new QueryExecutionOptions { EnableApproximateSearch = true });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<NativeAnnSettledRf3Tests>(nameof(
                    OriginalFullResultReplaysAcrossColdRf3ThenRefusesPriorParentAndChangedCurrentAuthority));
                await NativeAnnSettledRf3Flow.RunAsync(fixture);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
