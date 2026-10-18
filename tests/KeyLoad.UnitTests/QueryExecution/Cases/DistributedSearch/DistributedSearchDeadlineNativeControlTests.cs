
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchDeadlineNativeControlTests
{
    [Test]
    public Task ActualTwoOwnerCancellationAndPersistedDenialKeepOriginalMixedCauseThenFreshColdMergeIsHealthy()
        => QueryNativeFixtureAssertions.RunAsync(DistributedSearchDeadlineNativeControls.RequireAsync);
}
