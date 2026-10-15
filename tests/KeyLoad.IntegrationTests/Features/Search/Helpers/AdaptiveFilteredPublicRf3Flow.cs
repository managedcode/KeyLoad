using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => AdaptiveFilteredPublicRf3Seed.RunAsync(fixture, administrator, token), token);
        await WaveAsync(fixture, state, state.Pin, true, token);
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AdaptiveFilteredPublicRf3ColdOwners.RunAsync(fixture, token);
                var restored = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                    administrator => AdaptiveFilteredPublicRf3Publication.RestoreAsync(administrator, state, token), token);
                await WaveAsync(fixture, state, restored, false, token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, AdaptiveFilteredPublicRf3State state,
        AnnMaintenanceRequest pin, bool mutatePolicy, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, administrator =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Reader.Secret, async reader =>
            {
                await AdaptiveFilteredPublicRf3Reads.RunAsync(administrator, reader, state, pin, token);
                await AdaptiveFilteredPublicRf3Refusals.CancelAsync(administrator, reader, state, pin, token);
                if (mutatePolicy)
                {
                    await AdaptiveFilteredPublicRf3Policy.RunAsync(administrator, reader, state, pin, token);
                }
                await AdaptiveFilteredPublicRf3Current.ReplayAsync(administrator, state, token);
                return true;
            }, token), token);
}
