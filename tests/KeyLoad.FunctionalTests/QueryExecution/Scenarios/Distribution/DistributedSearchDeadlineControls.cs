using KeyLoad.Query.QueryExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class DistributedSearchDeadlineControls
{
    private const string Missing = "The original execution distributed refusal is unavailable.";

    internal static async Task RequireAsync(QueryFixture fixture)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var flow = new QueryDataset(fixture);
        flow.Seed();
        var source = QueryFixtureAssertions.Image(fixture.Source);
        var destination = QueryFixtureAssertions.Image(fixture.Destination);
        var sourcePosition = fixture.Source.Store.Position;
        var destinationPosition = fixture.Destination.Store.Position;
        PartitionQueryLeafResult? page = null;
        var leaf = new QueryLifetimeFlow(fixture, sourceNarrower: false);
        var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            page = leaf.Query(QueryFixture.Title, cancellationToken: token);
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException(Missing);
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await caller.CancelAsync().ConfigureAwait(false);
        var original = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
        {
            page = leaf.Query(QueryFixture.Title, cancellationToken: caller.Token);
            return Task.CompletedTask;
        }) ?? throw new InvalidOperationException(Missing);
        await Assert.That(original.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(page).IsNull();
        var cancelled = OrleansRpcFailure.Translate(original, command: false, Guid.NewGuid(),
            NullLogger.Instance, caller.Token);
        await Assert.That(cancelled.Code).IsEqualTo(ErrorCode.Cancelled);
        await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        flow.GrantDestination();
        source = QueryFixtureAssertions.Image(fixture.Source);
        destination = QueryFixtureAssertions.Image(fixture.Destination);
        sourcePosition = fixture.Source.Store.Position;
        destinationPosition = fixture.Destination.Store.Position;
        await DistributedSearchStatisticsAssertions.HealthyAsync(fixture, token);
        await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        await fixture.Source.ReopenAsync();
        await fixture.Destination.ReopenAsync();
        await DistributedSearchStatisticsAssertions.HealthyAsync(fixture, token);
        await QueryFixtureAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
    }
}
