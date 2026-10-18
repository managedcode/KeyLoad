
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchHybridNativeTests
{
    [Test]
    public Task GloballyRankedCompleteModalitiesPreserveIndependentWeightedRrfDocumentsAcrossBothColdOwners()
        => QueryNativeFixtureAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            DistributedSearchHybridNativeSeed.Seed(fixture);
            var source = QueryNativeFixtureAssertions.Image(fixture.Source);
            var destination = QueryNativeFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchHybridNativeAssertions.RequireAsync(fixture, token);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await fixture.Source.ReopenAsync();
            await fixture.Destination.ReopenAsync();
            await DistributedSearchHybridNativeAssertions.RequireAsync(fixture, token);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
