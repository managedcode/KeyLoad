using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class CompetingInboxSeed
{
    internal static async Task RunAsync(TestDatabase fixture, CompetingInboxState state, CancellationToken token)
    {
        fixture.Configure(CompetingInboxState.Queue, ResourceKind.WorkQueue);
        fixture.Configure(CompetingInboxState.Output, ResourceKind.WorkQueue);
        fixture.Configure(CompetingInboxState.Documents, ResourceKind.Collection);
        fixture.Configure(CompetingInboxState.Streams, ResourceKind.StreamSet);
        state.Producer = fixture.Commit(new EnqueueMessage(CompetingInboxState.Queue, "input", "{\"n\":1}"));
        var id = Guid.NewGuid();
        var claim = state.Operation(OperationKind.Receive, new ReceiveRequest(id, state.Lane), id);
        var original = fixture.Database.Apply(claim);
        var delivery = await Assert.That(original.Get<ReceiveResult>().Deliveries).HasSingleItem();
        state.Competitors = [.. Enumerable.Range(0, 2).Select(index =>
        {
            var request = new ProcessingRequest(Guid.NewGuid(), state.Lane, delivery.Token, "processor", 1, [.. state.Effects(index)]);
            return state.Operation(OperationKind.Processing, request, request.CommandId);
        })];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var tasks = state.Competitors.Select(operation => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                ready.SetResult();
            }
            await release.Task.WaitAsync(token);
            return fixture.Database.Apply(operation);
        }, token)).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ready.Task.WaitAsync(token);
            state.EffectPosition = fixture.Store.Position + 1;
        }, failures);
        release.TrySetResult();
        state.Results = new OperationResult[tasks.Length];
        for (var index = 0; index < tasks.Length; index++)
        {
            await ServerFailureObserver.ObserveAsync(async () => state.Results[index] = await tasks[index], failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var success = state.Results.First(result => result.Error is null).Get<CommitReceipt>();
        state.Winner = Array.FindIndex(state.Competitors, operation => operation.Id == success.CommandId);
        await Assert.That(state.Winner is 0 or 1).IsTrue();
        state.Receipt = success;
        await CompetingInboxAssertions.ResultsAsync(fixture.Database, fixture.Store, state);
    }
}
