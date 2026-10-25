using KeyLoad.Query.QueryExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class DistributedSearchDeadlineControls
{
    private const string Missing = "The original execution distributed refusal is unavailable.";
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

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
        await DecisionsAsync(cancelled, denied, caller.Token, token).ConfigureAwait(false);
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

    private static async Task DecisionsAsync(KeyLoadException cancelled, Exception denied,
        CancellationToken caller, CancellationToken fixture)
    {
        using var deadline = new CancellationTokenSource();
        var allCancelled = new AggregateException(new AggregateException(cancelled));
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(allCancelled, deadline.Token, fixture)).IsFalse();
        await deadline.CancelAsync().ConfigureAwait(false);
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(allCancelled, deadline.Token, caller)).IsFalse();
        var mixed = new AggregateException(allCancelled, denied);
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(mixed, deadline.Token, fixture)).IsFalse();
        var nestedDenial = Errors.Fail(ErrorCode.Cancelled, cancelled.Message, denied);
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(nestedDenial, deadline.Token, fixture)).IsFalse();
        await Assert.That(ReferenceEquals(nestedDenial.InnerException, denied)).IsTrue();
        await Assert.That(ReferenceEquals(mixed.InnerExceptions.First(), allCancelled)).IsTrue();
        await Assert.That(ReferenceEquals(mixed.InnerExceptions.Last(), denied)).IsTrue();
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(new AggregateException(), deadline.Token, fixture)).IsFalse();
        await Assert.That(DistributedSearchDeadlineFailure.IsOwnedDeadline(allCancelled, deadline.Token, fixture)).IsTrue();
        var mapped = Errors.Fail(ErrorCode.BudgetExceeded, DeadlineDetail, allCancelled);
        await Assert.That(ReferenceEquals(mapped.InnerException, allCancelled)).IsTrue();
        await Assert.That(ReferenceEquals(allCancelled.Flatten().InnerExceptions.Single(), cancelled)).IsTrue();
        await Assert.That(mapped.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(mapped.StatusCode).IsEqualTo(Errors.Status(ErrorCode.BudgetExceeded));
        await Assert.That(JsonDefaults.Serialize(mapped.ToProblem()).SequenceEqual(
            JsonDefaults.Serialize(Errors.Problem(ErrorCode.BudgetExceeded, DeadlineDetail)))).IsTrue();
    }
}
