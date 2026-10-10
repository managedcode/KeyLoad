using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class DistributedSearchStatisticsNativeTests
{
    private const long UpdatedPolicyEpoch = 4;
    private const string MissingFailure = "The old distributed statistics cut did not retain its rejection.";
    private const string CutChanged = "The distributed search statistics cut or authority changed.";

    [Test]
    public async Task IndependentCanonicalCorporaProduceGlobalBm25AndCompleteRedactedLiteralDocuments()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, TestContext.Current!.Execution.CancellationToken);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });

    [Test]
    public async Task ActualPersistedPolicyChangeRejectsOriginalStatisticsBeforeProjectionThenFreshAndColdOwnersAreHealthy()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var token = TestContext.Current!.Execution.CancellationToken;
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var old = DistributedSearchStatisticsNativeFlow.Capture(fixture.Source,
                RemotePartitionQueryNativeFlow.Local, PhysicalOwnerDirectoryWholeFlow.Control.Owner, token);
            var remote = DistributedSearchStatisticsNativeFlow.Capture(fixture.Destination,
                RemoteDocumentNativeFixture.Partition, PhysicalOwnerDirectoryWholeFlow.Destination.Owner, token);
            var statistics = DistributedSearchStatisticsNativeFlow.Combine(fixture.Source, old, remote, token);
            flow.SetSourceGrant(UpdatedPolicyEpoch, granted: true);
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
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
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, token);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
            fixture.Source.Reopen();
            fixture.Destination.Reopen();
            await DistributedSearchStatisticsNativeAssertions.HealthyAsync(fixture, token);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        });
}
