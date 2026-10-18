
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchStatisticsNativeTests
{
    private const long UpdatedPolicyEpoch = 4;
    private const string MissingFailure = "The old distributed statistics cut did not retain its rejection.";
    private const string CutChanged = "The distributed search statistics cut or authority changed.";

    [Test]
    public async Task IndependentCanonicalCorporaProduceGlobalBm25AndCompleteRedactedLiteralDocuments()
        => await QueryNativeFixtureAssertions.RunAsync(async fixture =>
        {
            var flow = new QueryNativePartitionFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = QueryNativeFixtureAssertions.Image(fixture.Source);
            var destination = QueryNativeFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, TestContext.Current!.Execution.CancellationToken);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });

    [Test]
    public async Task ActualPersistedPolicyChangeRejectsOriginalStatisticsBeforeProjectionThenFreshAndColdOwnersAreHealthy()
        => await QueryNativeFixtureAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var flow = new QueryNativePartitionFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var old = DistributedSearchStatisticsNativeFlow.Capture(fixture.Source,
                QueryNativePartitionFlow.Local, fixture.SourceOwner, token);
            var remote = DistributedSearchStatisticsNativeFlow.Capture(fixture.Destination,
                QueryNativeFixture.Partition, fixture.DestinationOwner, token);
            var statistics = DistributedSearchStatisticsNativeFlow.Combine(fixture.Source, old, remote, token);
            flow.SetSourceGrant(UpdatedPolicyEpoch, granted: true);
            var source = QueryNativeFixtureAssertions.Image(fixture.Source);
            var destination = QueryNativeFixtureAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            RankedDocument[]? page = null;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                page = DistributedSearchStatisticsNativeFlow.Rank(fixture.Source, old, statistics, token);
                return Task.CompletedTask;
            }) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(failure.Message).IsEqualTo(CutChanged);
            await Assert.That(page).IsNull();
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, token);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await fixture.Source.ReopenAsync();
            await fixture.Destination.ReopenAsync();
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, token);
            await QueryNativeFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
