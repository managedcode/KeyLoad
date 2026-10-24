using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class ModelRootClusterFlow
{
    private const long NoEventSequence = 0;

    // The event sequence is partition-wide, so it continues after the events already in the partition.
    internal static async Task RunAsync(ClusterFixture fixture, PartitionRef partition, long priorEvents, CancellationToken token)
    {
        var states = new List<ModelRootClusterState>();
        var events = priorEvents;
        await RootAsync(fixture, async root =>
        {
            foreach (var queue in new[] { false, true })
            {
                foreach (var header in new[] { false, true })
                {
                    var sequence = queue ? NoEventSequence : ++events;
                    var state = await ModelRootClusterSeed.RunAsync(root, partition, queue, header, sequence, token);
                    states.Add(state);
                    await WaveAsync(fixture, root, state, false, false, token);
                    await ModelRootClusterSeed.ConfigureAsync(root, state, 2L, true, false, token);
                    await WaveAsync(fixture, root, state, true, false, token);
                    await ModelRootClusterSeed.ConfigureAsync(root, state, 3L, false, false, token);
                    await WaveAsync(fixture, root, state, false, false, token);
                    await ModelRootClusterSeed.ConfigureAsync(root, state, 4L, true, true, token);
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
                await ModelSqlClusterCold.RunAsync(fixture, token);
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

    private static Task ReplayAsync(RequestCqrsClusterCallers root, ModelRootClusterState state, CancellationToken token)
        => ModelRootClusterWave.ObserveAsync(root, state, async () => await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token))), token);
    private static async Task WaveAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, ModelRootClusterState state,
        bool use, bool read, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Key, async reader =>
        {
            await ModelRootClusterWave.RunAsync(root, reader, state, use, read, token);
            return true;
        }, token);
    private static async Task RootAsync(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await operation(root);
            return true;
        }, token);
}
