
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchDeadlineControlTests
{
    [Test]
    public Task LeafCancellationAndPersistedDenialKeepOriginalMixedCauseThenFreshColdOwnersAreHealthy()
        => QueryFixtureAssertions.RunAsync(DistributedSearchDeadlineControls.RequireAsync);
}
