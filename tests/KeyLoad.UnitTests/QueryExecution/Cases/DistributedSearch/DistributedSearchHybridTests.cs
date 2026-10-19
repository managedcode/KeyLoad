
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchHybridTests
{
    [Test]
    public Task GloballyRankedCompleteModalitiesPreserveIndependentWeightedRrfDocumentsAcrossBothColdOwners()
        => QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            DistributedSearchHybridSeed.Seed(fixture);
            var source = QueryFixtureAssertions.Image(fixture.Source);
            var destination = QueryFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchHybridAssertions.RequireAsync(fixture, token);
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await fixture.Source.ReopenAsync();
            await fixture.Destination.ReopenAsync();
            await DistributedSearchHybridAssertions.RequireAsync(fixture, token);
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
