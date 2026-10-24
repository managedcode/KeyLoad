using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class AggregateSnapshotRaceClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        AggregateSnapshotRaceClusterState? state = null;
        var failures = new List<Exception>();
        var cold = new AggregateSnapshotRaceClusterCold();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
            {
                state = await AggregateSnapshotRaceClusterSeed.RunAsync(fixture, root, token);
                await ReaderAsync(fixture, state, async reader =>
                {
                    await AggregateSnapshotRaceClusterCompetitors.RunAsync(reader, state, token);
                    state.OriginalImage = await AggregateSnapshotRaceClusterCurrent.ImageAsync(root, state, token);
                    await AggregateSnapshotRaceClusterCurrent.ObserveAsync(root, state, async () =>
                    {
                        await AggregateSnapshotRaceClusterCurrent.RequireAsync(reader, state, token);
                        await AggregateSnapshotRaceClusterFailures.CommandsAsync(reader, state, false, token);
                    }, token);
                    await AggregateSnapshotRaceClusterPolicy.RepairAsync(root, reader, state, token);
                }, token);
                return true;
            }, token);
        }, failures);
        var directory = fixture.Root;
        NodeStatus[]? before = null;
        await ServerFailureObserver.ObserveAsync(async () => before = await AggregateSnapshotRaceClusterCold.StatusesAsync(fixture, token), failures);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(() => cold.RestartAsync(fixture, token), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(fixture.Root).IsEqualTo(directory);
                await Assert.That(Directory.Exists(directory)).IsTrue();
                await AggregateSnapshotRaceClusterCold.IdentitiesAsync(before ?? throw new InvalidOperationException("The original three-node identity baseline is unavailable."),
                    await AggregateSnapshotRaceClusterCold.StatusesAsync(fixture, token));
                if (state is not null)
                {
                    await WaveAsync(fixture, state, token);
                }
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => cold.RequireAsync(token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => cold.RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, AggregateSnapshotRaceClusterState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root => { await ReaderAsync(fixture, state, reader => AggregateSnapshotRaceClusterPolicy.WaveAsync(root, reader, state, token), token); return true; }, token);
    private static Task<bool> ReaderAsync(ClusterFixture fixture, AggregateSnapshotRaceClusterState state,
        Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Scenario.WorkerSecret,
            async reader => { await action(reader); return true; }, token);
}
