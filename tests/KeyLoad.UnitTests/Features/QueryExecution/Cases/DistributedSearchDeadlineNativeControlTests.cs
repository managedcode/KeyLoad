using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchDeadlineNativeControlTests
{
    [Test]
    public Task ActualTwoOwnerCancellationAndPersistedDenialKeepOriginalMixedCauseThenFreshColdMergeIsHealthy()
        => RemoteDocumentNativeAssertions.RunAsync(DistributedSearchDeadlineNativeControls.RequireAsync);
}
