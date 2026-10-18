using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, string kind, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                var seeded = await RowRevisionInferenceClusterSeed.CreateAsync(root, token);
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, seeded.Key,
                    async reader =>
                    {
                        await RowRevisionInferenceClusterAttempts.RunAsync(root, reader, seeded, kind, token);
                        await RowRevisionInferenceClusterRecovery.RunAsync(root, reader, seeded, kind, token);
                        return true;
                    }, token);
                return seeded;
            }, token);
        var failures = new List<Exception>();
        for (var cycle = P.First; cycle < P.ColdCycles; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlClusterCold.RunAsync(fixture, token);
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                    async root => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Key,
                        async reader =>
                        {
                            await RowRevisionInferenceClusterAttempts.ReplayAsync(root, reader, state, token);
                            await RowRevisionInferenceClusterRecovery.ReplayAsync(root, reader, state, token);
                            await RowRevisionInferenceClusterCurrent.RootReplayAsync(root, state, token);
                            await RowRevisionInferenceClusterRecovery.ReadsAsync(root, reader, state, token);
                            return true;
                        }, token), token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
