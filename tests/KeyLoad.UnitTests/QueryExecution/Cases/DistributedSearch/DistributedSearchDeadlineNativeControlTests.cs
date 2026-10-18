
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchDeadlineNativeControlTests
{
    [Test]
    public Task NativeLeafCancellationAndPersistedDenialKeepOriginalMixedCauseThenFreshColdOwnersAreHealthy()
        => QueryNativeFixtureAssertions.RunAsync(DistributedSearchDeadlineNativeControls.RequireAsync);
}
