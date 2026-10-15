using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class TransactionalReadViewFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var state = new TransactionalReadViewState();
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(state, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RunOwnedAsync(TransactionalReadViewState state, CancellationToken token)
    {
        await TransactionalReadViewCommands.SeedAsync(state);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewConcurrency.RunAsync(state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewRefusals.CancellationAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewContinuation.HealthyAsync(state, token), failures);
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Reopen();
                await TransactionalReadViewContinuation.RequireAsync(state, token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
