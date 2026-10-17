using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.DocumentStorage;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchDeadlineNativeControls
{
    private const string Missing = "The original native distributed refusal is unavailable.";
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    internal static async Task RequireAsync(RemoteDocumentNativeFixture fixture)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var flow = new RemotePartitionQueryNativeFlow(fixture);
        flow.Seed();
        var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
        var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var sourcePosition = fixture.Source.Store.Position;
        var destinationPosition = fixture.Destination.Store.Position;
        PartitionQueryPageV1? page = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            page = await flow.ExecuteAsync(token).ConfigureAwait(false), failures).ConfigureAwait(false);
        var denied = failures.Single();
        IReadOnlyList<Exception> deniedLeaves = denied is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [denied];
        await Assert.That(deniedLeaves.Any(failure => failure is KeyLoadException { Code: ErrorCode.PermissionDenied })).IsTrue();
        await Assert.That(deniedLeaves.All(failure => failure is KeyLoadException { Code: ErrorCode.PermissionDenied }
            or OperationCanceledException)).IsTrue();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await caller.CancelAsync().ConfigureAwait(false);
        var original = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            page = await flow.ExecuteAsync(caller.Token)) ?? throw new InvalidOperationException(Missing);
        await Assert.That(original.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(page).IsNull();
        var cancelled = OrleansRpcFailure.Translate(original, command: false, Guid.NewGuid(),
            NullLogger.Instance, caller.Token);
        await Assert.That(cancelled.Code).IsEqualTo(ErrorCode.Cancelled);
        await DecisionsAsync(cancelled, denied, caller.Token, token).ConfigureAwait(false);
        await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        flow.GrantDestination();
        source = RemoteDocumentNativeAssertions.Image(fixture.Source);
        destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        sourcePosition = fixture.Source.Store.Position;
        destinationPosition = fixture.Destination.Store.Position;
        await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(token), fixture);
        await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
        fixture.Source.Reopen();
        fixture.Destination.Reopen();
        var cold = new RemotePartitionQueryNativeFlow(fixture);
        await RemotePartitionQueryNativeAssertions.PageAsync(await cold.ExecuteAsync(token), fixture);
        await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination, sourcePosition, destinationPosition);
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
