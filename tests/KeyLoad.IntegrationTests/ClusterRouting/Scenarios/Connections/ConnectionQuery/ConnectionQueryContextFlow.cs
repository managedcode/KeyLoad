using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextFlow
{
    internal static async Task RunAsync(ConnectionRf3Scenario scenario, CancellationToken token)
    {
        var state = await ConnectionQueryContextProvisioning.RunAsync(scenario, token);
        var caller = await scenario.CallerAsync(token, multiplexed: true);
        var second = caller.WithCredential(state.Second.Identity.Secret);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        var held = await scenario.StartHeldAsync(state.First.Identity, Guid.Empty, GrainReadKind.Query,
            () => caller.Sdk.QueryAsync(ConnectionQueryContextRequests.Sql(state.First.Identity.Partition), selected.Token), token);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            await ConnectionQueryContextPages.SdkAsync(scenario, state, second, state.Second, held.Witness, token);
            await Assert.That(held.Original.IsCompleted).IsFalse();
        }, token);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state,
            () => CancelAsync(scenario, held, selected, token), token);
        var first = await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, held.Witness, token);
        await ConnectionQueryContextPolicy.RunAsync(scenario, state, caller, second, first, token);
        await ReconnectAsync(scenario, state, caller, first, token);
        await OfficialAsync(scenario, state, token);
        await ColdAsync(scenario, state, token);
    }
    private static async Task ColdAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var ordinal = 1; ordinal <= 2; ordinal++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await scenario.RestartQueryContextsColdAsync(ordinal, token);
                var caller = await scenario.CallerAsync(token, multiplexed: true);
                var first = await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, null, token);
                await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.WithCredential(state.Second.Identity.Secret),
                    state.Second, first.Witness, token);
                await scenario.CloseAsync(caller, first.Marker, token);
                var official = await scenario.CallerAsync(token);
                var page = await ConnectionQueryContextPages.McpAsync(scenario, state, official, token);
                await scenario.CloseAsync(official, page.Marker, token);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => scenario.RequireQueryColdReceiptsAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CancelAsync(ConnectionRf3Scenario scenario, ConnectionRf3HeldOperation<Result<QueryPage>> held,
        CancellationTokenSource selected, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await ConnectionQueryContextCalls.ErrorAsync(await held.Original, ErrorCode.Cancelled), failures);
        await ServerFailureObserver.ObserveAsync(() => scenario.SettleAsync(held.Arm, held.Marker, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ReconnectAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        ConnectionRf3Caller caller, ConnectionQueryContextRead original, CancellationToken token)
    {
        await scenario.CloseAsync(caller, original.Marker, token);
        var fresh = await scenario.CallerAsync(token, multiplexed: true);
        var current = await ConnectionQueryContextPages.SdkAsync(scenario, state, fresh.Sdk, state.First, null, token);
        await Assert.That(current.Witness.ConnectionId).IsNotEqualTo(original.Witness.ConnectionId);
        await Assert.That(current.Witness.GrainId).IsNotEqualTo(original.Witness.GrainId);
        await Assert.That(current.Witness.ActivationId).IsNotEqualTo(original.Witness.ActivationId);
        await ConnectionQueryContextPages.SdkAsync(scenario, state, fresh.WithCredential(state.Second.Identity.Secret),
            state.Second, current.Witness, token);
        await scenario.CloseAsync(fresh, current.Marker, token);
    }
    private static Task OfficialAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, CancellationToken token)
        => ConnectionQueryContextOfficial.RunAsync(scenario, state, token);
}
