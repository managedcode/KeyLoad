using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, string kind, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                var seeded = await RowRevisionInferenceRf3Seed.CreateAsync(root, token);
                await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, seeded.Key,
                    async reader =>
                    {
                        await RowRevisionInferenceRf3Attempts.RunAsync(root, reader, seeded, kind, token);
                        await RowRevisionInferenceRf3Recovery.RunAsync(root, reader, seeded, kind, token);
                        return true;
                    }, token);
                return seeded;
            }, token);
        var failures = new List<Exception>();
        for (var cycle = P.First; cycle < P.ColdCycles; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlRf3Cold.RunAsync(fixture, token);
                await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                    async root => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Key,
                        async reader =>
                        {
                            await RowRevisionInferenceRf3Attempts.ReplayAsync(root, reader, state, token);
                            await RowRevisionInferenceRf3Recovery.ReplayAsync(root, reader, state, token);
                            await RowRevisionInferenceRf3Current.RootReplayAsync(root, state, token);
                            await RowRevisionInferenceRf3Recovery.ReadsAsync(root, reader, state, token);
                            return true;
                        }, token), token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
