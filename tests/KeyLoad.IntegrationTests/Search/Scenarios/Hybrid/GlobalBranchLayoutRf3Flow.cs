using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Flow
{
    internal static async Task RunAsync(bool official, CancellationToken token)
    {
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        var evidence = new GlobalBranchLayoutRf3FaultEvidence();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
            var state = await WarmAsync(wave, official, evidence, token);
            await GlobalBranchLayoutRf3Cold.RunAsync(wave, state, evidence, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => evidence.RequireAsync(token), failures);
        if (wave is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<GlobalBranchLayoutRf3State> WarmAsync(TwoRf3MembershipWave wave, bool official, GlobalBranchLayoutRf3FaultEvidence evidence, CancellationToken token)
    {
        var failures = new List<Exception>();
        GlobalBranchLayoutRf3State state = null!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state = await GlobalBranchLayoutRf3Setup.RunAsync(wave, callers, token);
                await GlobalBranchLayoutRf3Calls.HealthyAsync(wave, callers, state, 3L, null, token);
                await GlobalBranchLayoutRf3Receipts.RequireAsync(wave, callers, state, token);
                await GlobalBranchLayoutRf3Policy.RunAsync(wave, callers, state, 4L, 5L, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await GlobalBranchLayoutRf3MissingOwner.RunAsync(wave, state, official, evidence, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await GlobalBranchLayoutRf3Calls.HealthyAsync(wave, callers, state, 5L, null, token);
                state = await GlobalBranchLayoutRf3Move.RunAsync(wave, callers, state, token);
                await GlobalBranchLayoutRf3Calls.HealthyAsync(wave, callers, state, 5L, null, token);
                await GlobalBranchLayoutRf3Policy.RunAsync(wave, callers, state, 6L, 7L, token);
                await GlobalBranchLayoutRf3Receipts.RequireAsync(wave, callers, state, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return state;
    }
}
