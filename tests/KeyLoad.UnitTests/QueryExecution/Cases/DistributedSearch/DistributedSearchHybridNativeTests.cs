using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchHybridNativeTests
{
    [Test]
    public Task GloballyRankedCompleteModalitiesPreserveIndependentWeightedRrfDocumentsAcrossBothColdOwners()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            DistributedSearchHybridNativeSeed.Seed(fixture);
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchHybridNativeAssertions.RequireAsync(fixture, token);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            fixture.Source.Reopen();
            fixture.Destination.Reopen();
            await DistributedSearchHybridNativeAssertions.RequireAsync(fixture, token);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
