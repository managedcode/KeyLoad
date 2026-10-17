using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, PartitionRef partition, CancellationToken token)
    {
        var states = new List<ModelRootRf3State>();
        await RootAsync(fixture, async root =>
        {
            foreach (var queue in new[] { false, true })
            {
                foreach (var header in new[] { false, true })
                {
                    var state = await ModelRootRf3Seed.RunAsync(root, partition, queue, header, token);
                    states.Add(state);
                    await WaveAsync(fixture, root, state, false, false, token);
                    await ModelRootRf3Seed.ConfigureAsync(root, state, 2L, true, false, token);
                    await WaveAsync(fixture, root, state, true, false, token);
                    await ModelRootRf3Seed.ConfigureAsync(root, state, 3L, false, false, token);
                    await WaveAsync(fixture, root, state, false, false, token);
                    await ModelRootRf3Seed.ConfigureAsync(root, state, 4L, true, true, token);
                    await WaveAsync(fixture, root, state, true, true, token);
                    await ReplayAsync(root, state, token);
                }
            }
        }, token);
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlRf3Cold.RunAsync(fixture, token);
                await RootAsync(fixture, async root =>
                {
                    foreach (var state in states)
                    {
                        await WaveAsync(fixture, root, state, true, true, token);
                        await ReplayAsync(root, state, token);
                    }
                }, token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task ReplayAsync(RequestCqrsRf3Callers root, ModelRootRf3State state, CancellationToken token)
        => ModelRootRf3Wave.ObserveAsync(root, state, async () => await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token))), token);
    private static async Task WaveAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, ModelRootRf3State state,
        bool use, bool read, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Key, async reader =>
        {
            await ModelRootRf3Wave.RunAsync(root, reader, state, use, read, token);
            return true;
        }, token);
    private static async Task RootAsync(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await operation(root);
            return true;
        }, token);
}
