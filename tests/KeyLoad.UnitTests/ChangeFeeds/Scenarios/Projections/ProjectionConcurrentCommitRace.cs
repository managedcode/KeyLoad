using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionConcurrentCommitRace
{
    internal static async Task<(OperationResult First, OperationResult Second)> RunAsync(DatabaseEngine owner,
        ReplicatedOperation first, ReplicatedOperation second, CancellationToken token)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await gate.Task.WaitAsync(token);
            return owner.ApplyEmbedded(first, token);
        }, token);
        var secondTask = Task.Run(async () =>
        {
            await gate.Task.WaitAsync(token);
            return owner.ApplyEmbedded(second, token);
        }, token);
        gate.SetResult();
        var failures = new List<Exception>();
        OperationResult? firstResult = null;
        OperationResult? secondResult = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { firstResult = await firstTask; }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { secondResult = await secondTask; }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return (firstResult ?? throw new InvalidOperationException(MissingResult),
            secondResult ?? throw new InvalidOperationException(MissingResult));
    }

    private const string MissingResult = "The joined native projection competitors require both original outcomes.";
}
