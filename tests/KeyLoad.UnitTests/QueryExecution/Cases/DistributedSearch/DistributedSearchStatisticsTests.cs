
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchStatisticsTests
{
    private const long UpdatedPolicyEpoch = 4;
    private const string MissingFailure = "The old distributed statistics cut did not retain its rejection.";
    private const string CutChanged = "The distributed search statistics cut or authority changed.";

    [Test]
    public async Task IndependentCanonicalCorporaProduceGlobalBm25AndCompleteRedactedLiteralDocuments()
        => await QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryDataset(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = QueryFixtureAssertions.Image(fixture.Source);
            var destination = QueryFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchStatisticsAssertions.HealthyAsync(fixture, TestContext.Current!.Execution.CancellationToken);
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });

    [Test]
    public async Task ActualPersistedPolicyChangeRejectsOriginalStatisticsBeforeProjectionThenFreshAndColdOwnersAreHealthy()
        => await QueryFixtureAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var flow = new QueryDataset(fixture);
            flow.Seed();
            flow.GrantDestination();
            var old = DistributedSearchStatisticsFlow.Capture(fixture.Source,
                QueryDataset.Local, fixture.SourceOwner, token);
            var remote = DistributedSearchStatisticsFlow.Capture(fixture.Destination,
                QueryFixture.Partition, fixture.DestinationOwner, token);
            var statistics = DistributedSearchStatisticsFlow.Combine(fixture.Source, old, remote, token);
            flow.SetSourceGrant(UpdatedPolicyEpoch, granted: true);
            var source = QueryFixtureAssertions.Image(fixture.Source);
            var destination = QueryFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            RankedDocument[]? page = null;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                page = DistributedSearchStatisticsFlow.Rank(fixture.Source, old, statistics, token);
                return Task.CompletedTask;
            }) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(failure.Message).IsEqualTo(CutChanged);
            await Assert.That(page).IsNull();
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await DistributedSearchStatisticsAssertions.HealthyAsync(fixture, token);
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await fixture.Source.ReopenAsync();
            await fixture.Destination.ReopenAsync();
            await DistributedSearchStatisticsAssertions.HealthyAsync(fixture, token);
            await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
