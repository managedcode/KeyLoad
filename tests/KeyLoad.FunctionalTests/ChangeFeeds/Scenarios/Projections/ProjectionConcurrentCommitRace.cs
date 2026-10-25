using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class ProjectionConcurrentCommitRace
{
    internal static async Task<(Result<object> First, Result<object> Second)> RunAsync(DatabaseEngine owner,
        Command<DatabaseCommand> first, Command<DatabaseCommand> second, CancellationToken token)
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
        Result<object>? firstResult = null;
        Result<object>? secondResult = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { firstResult = await firstTask; }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () => { secondResult = await secondTask; }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return (firstResult ?? throw new InvalidOperationException(MissingResult),
            secondResult ?? throw new InvalidOperationException(MissingResult));
    }

    private const string MissingResult = "The joined execution projection competitors require both original outcomes.";
}
