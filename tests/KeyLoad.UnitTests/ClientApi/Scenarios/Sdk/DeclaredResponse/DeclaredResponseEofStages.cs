using KeyLoad.Client;
using KeyLoad.UnitTests.Features.OrleansExecution;
using static KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class DeclaredResponseEofStages
{
    internal static async Task RunAsync(ConnectionScenario scenario, PrincipalRecord principal,
        KeyLoadClientKestrelServer server, DeclaredResponseEofEndpoint endpoint, DeclaredResponseEofTasks tasks,
        AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        var client = new KeyLoadClient(server.Client, ApiKey, UnitClientOptions.Execution());
        var original = tasks.Retain(client.CommitAsync(endpoint.Command, token));
        var handler = await PartialAsync(endpoint.Write, original, tasks, token);
        await Assert.That(original.IsCompleted).IsFalse();
        var expected = await DeclaredResponseEofAssertions.FirstAsync(scenario, principal,
            endpoint.Command, endpoint.Original, placement);
        var cut = DeclaredResponseEofAssertions.Capture(scenario, principal, endpoint.Command);
        endpoint.Write.Release.TrySetResult();
        var refused = await tasks.CompleteAsync(original);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(refused.Problem, ErrorCode.UnknownWriteOutcome, WriteDetail);
        await endpoint.Write.Aborted.Task.WaitAsync(token);
        await tasks.JoinAsync(handler);
        await DeclaredResponseEofAssertions.CurrentAsync(scenario, principal, endpoint.Command, cut);
        var replay = await tasks.CompleteAsync(tasks.Retain(client.CommitAsync(endpoint.Command, token)));
        await Assert.That(replay.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(expected, replay.Value);
        await DeclaredResponseEofAssertions.CurrentAsync(scenario, principal, endpoint.Command, cut);
        await ReadAsync(scenario, principal, client, endpoint, tasks, cut, token);
        await endpoint.RequireCountsAsync();
    }

    private static async Task ReadAsync(ConnectionScenario scenario, PrincipalRecord principal,
        KeyLoadClient client, DeclaredResponseEofEndpoint endpoint, DeclaredResponseEofTasks tasks,
        DeclaredResponseEofCut cut, CancellationToken token)
    {
        var original = tasks.Retain(client.GetAsync(scenario.Reference(Document), token));
        var handler = await PartialAsync(endpoint.Read, original, tasks, token);
        await Assert.That(original.IsCompleted).IsFalse();
        endpoint.Read.Release.TrySetResult();
        var refused = await tasks.CompleteAsync(original);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(refused.Problem, ErrorCode.OwnershipLost, ReadDetail);
        await endpoint.Read.Aborted.Task.WaitAsync(token);
        await tasks.JoinAsync(handler);
        await DeclaredResponseEofAssertions.CurrentAsync(scenario, principal, endpoint.Command, cut);
        var healthy = await tasks.CompleteAsync(tasks.Retain(client.GetAsync(scenario.Reference(Document), token)));
        await Assert.That(healthy.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(
            new DocumentReadResult(new(scenario.Reference(Document), Revision, OriginalJson, false, [])), healthy.Value);
        await DeclaredResponseEofAssertions.CurrentAsync(scenario, principal, endpoint.Command, cut);
    }

    private static async Task<Task> PartialAsync(DeclaredResponseEofBoundary boundary, Task caller,
        DeclaredResponseEofTasks tasks, CancellationToken token)
    {
        await Task.WhenAny(boundary.Retained.Task, caller).WaitAsync(token);
        if (!boundary.Retained.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(Failure);
        }
        var handler = await boundary.Retained.Task.WaitAsync(token);
        await Task.WhenAny(boundary.Flushed.Task, handler).WaitAsync(token);
        if (handler.IsCompleted)
        {
            await tasks.JoinAsync(handler);
        }
        await boundary.Flushed.Task.WaitAsync(token);
        return handler;
    }
}
