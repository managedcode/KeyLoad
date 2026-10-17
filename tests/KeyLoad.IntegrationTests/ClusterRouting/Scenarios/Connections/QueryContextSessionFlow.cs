using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class QueryContextSessionFlow
{
    private const string MissingSeed = "The original complete query context seed did not settle.";

    internal static async Task RunAsync(ConnectionRf3Scenario scenario, CancellationToken token)
    {
        ConnectionQueryContextState? state = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            state = await ConnectionQueryContextProvisioning.RunAsync(scenario, token);
            var caller = await scenario.CallerAsync(token, multiplexed: true);
            var completed = await QueryContextSessionOverlap.RunAsync(scenario, state, caller, token);
            await ConnectionQueryContextPolicy.RunAsync(scenario, state, caller,
                caller.WithCredential(state.Second.Identity.Secret), completed, token);
            await ReconnectAsync(scenario, state, caller, completed, token);
            await ConnectionQueryContextOfficial.RunAsync(scenario, state, token);
        }, failures);
        if (state is not null)
        {
            await QueryContextSessionCold.RunAsync(scenario, state, failures, token);
        }
        else
        {
            failures.Add(new InvalidOperationException(MissingSeed));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ReconnectAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, ConnectionRf3Caller caller,
        ConnectionQueryContextRead original, CancellationToken token)
    {
        await scenario.CloseAsync(caller, original.Marker, token);
        var fresh = await scenario.CallerAsync(token, multiplexed: true);
        var current = await ConnectionQueryContextPages.SdkAsync(scenario, state, fresh.Sdk, state.First, null, token);
        await Assert.That(current.Witness.ConnectionId).IsNotEqualTo(original.Witness.ConnectionId);
        await Assert.That(current.Witness.GrainId).IsNotEqualTo(original.Witness.GrainId);
        await Assert.That(current.Witness.ActivationId).IsNotEqualTo(original.Witness.ActivationId);
        await ConnectionQueryContextPages.SdkAsync(scenario, state, fresh.WithCredential(state.Second.Identity.Secret), state.Second, current.Witness, token);
        await scenario.CloseAsync(fresh, current.Marker, token);
    }
}
