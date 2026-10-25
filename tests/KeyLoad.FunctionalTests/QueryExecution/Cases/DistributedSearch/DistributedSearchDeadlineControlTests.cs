
namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class DistributedSearchDeadlineControlTests
{
    [Test]
    public Task LeafCancellationAndPersistedDenialKeepOriginalMixedCauseThenFreshColdOwnersAreHealthy()
        => QueryFixtureAssertions.RunAsync(DistributedSearchDeadlineControls.RequireAsync);
}
