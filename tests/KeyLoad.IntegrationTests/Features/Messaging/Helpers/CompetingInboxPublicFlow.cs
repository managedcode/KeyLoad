using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class CompetingInboxPublicFlow
{
    internal static async Task RunAsync(CompetingInboxPublicState state, CancellationToken token)
    {
        await CompetingInboxPublicCallers.WithRootAsync(state, root => CompetingInboxPublicSeed.RunAsync(state, root, token), token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicCallers.WithAsync(state, async (root, sdk, official) =>
        {
            var warmFailures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicRace.RunAsync(state, sdk, official, token), warmFailures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.OutboxAsync(state, root,
                CompetingInboxPublicProtocol.FirstOutboxRecords, true, token), warmFailures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), warmFailures);
            await ServerFailureObserver.ObserveAsync(() => BeforePolicyAsync(state, root, sdk, official, token), warmFailures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicPolicy.RepairAsync(state, root, sdk, official, token), warmFailures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.StaleCurrent = state.Candidates[state.WinnerIndex] with
                {
                    CommandId = Guid.NewGuid(),
                    ExecutionGeneration = CompetingInboxPublicProtocol.StaleExecutionGeneration,
                    Effects = [.. CompetingInboxPublicState.Effects(CompetingInboxPublicProtocol.StaleEffect, CompetingInboxPublicProtocol.SecondJson)]
                };
                await CompetingInboxPublicPolicy.RefuseAsync(state, root, sdk, official, state.StaleCurrent,
                    ErrorCode.StaleLease, CompetingInboxPublicProtocol.Stale, token);
            }, warmFailures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicHealthy.RunAsync(state, root, sdk, token), warmFailures);
            ServerFailureObserver.ThrowIfAny(warmFailures);
        }, token), failures);
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(state, token), failures);
        for (var cold = 0; cold < CompetingInboxPublicProtocol.ColdCycles; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => state.Cold.RunAsync(token), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(state, token), failures);
        }
        using var cleanup = new CancellationTokenSource(
            KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Protocol.CleanupDeadline, state.Clock);
        await ServerFailureObserver.ObserveAsync(() => state.Cold.RequireAsync(cleanup.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task BeforePolicyAsync(CompetingInboxPublicState state,
        KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root,
        KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers sdk,
        KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers official, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var index = 0; index < state.Candidates.Length; index++)
        {
            var request = state.Candidates[index];
            if (state.Equivalent || index == state.WinnerIndex)
            {
                await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(state.Winner,
                    await McpCallerAssertions.SdkSuccessAsync(await sdk.Sdk.CommitProcessingAsync(request, token))), failures);
                await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
                await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(state.Winner,
                    (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.Mcp.CallAsync(McpCallerTools.MessagesProcess, request, token))).Value), failures);
                await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
            }
            else
            {
                await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicPolicy.RefuseAsync(state, root, sdk, official,
                    request, ErrorCode.Conflict, CompetingInboxPublicProtocol.Conflict, token), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task WaveAsync(CompetingInboxPublicState state, CancellationToken token)
        => CompetingInboxPublicCallers.WithAsync(state, async (root, sdk, official) =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicStatus.RequireAsync(state, token), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicReplay.RunAsync(state, root, sdk, official, token), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
            await ServerFailureObserver.ObserveAsync(() => state.Cold.RequireAsync(token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, token);
}
