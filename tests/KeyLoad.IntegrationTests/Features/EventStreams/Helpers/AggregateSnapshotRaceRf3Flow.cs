using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        AggregateSnapshotRaceRf3State? state = null;
        var failures = new List<Exception>();
        var cold = new AggregateSnapshotRaceRf3Cold();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
            {
                state = await AggregateSnapshotRaceRf3Seed.RunAsync(fixture, root, token);
                await ReaderAsync(fixture, state, async reader =>
                {
                    await AggregateSnapshotRaceRf3Competitors.RunAsync(reader, state, token);
                    state.OriginalImage = await AggregateSnapshotRaceRf3Current.ImageAsync(root, state, token);
                    await AggregateSnapshotRaceRf3Current.ObserveAsync(root, state, async () =>
                    {
                        await AggregateSnapshotRaceRf3Current.RequireAsync(reader, state, token);
                        await AggregateSnapshotRaceRf3Failures.CommandsAsync(reader, state, false, token);
                    }, token);
                    await AggregateSnapshotRaceRf3Policy.RepairAsync(root, reader, state, token);
                }, token);
                return true;
            }, token);
        }, failures);
        var directory = fixture.Root;
        NodeStatus[]? before = null;
        await ServerFailureObserver.ObserveAsync(async () => before = await AggregateSnapshotRaceRf3Cold.StatusesAsync(fixture, token), failures);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(() => cold.RestartAsync(fixture, token), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(fixture.Root).IsEqualTo(directory);
                await Assert.That(Directory.Exists(directory)).IsTrue();
                await AggregateSnapshotRaceRf3Cold.IdentitiesAsync(before ?? throw new InvalidOperationException("The original three-node identity baseline is unavailable."),
                    await AggregateSnapshotRaceRf3Cold.StatusesAsync(fixture, token));
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
    private static Task<bool> WaveAsync(ClusterFixture fixture, AggregateSnapshotRaceRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root => { await ReaderAsync(fixture, state, reader => AggregateSnapshotRaceRf3Policy.WaveAsync(root, reader, state, token), token); return true; }, token);
    private static Task<bool> ReaderAsync(ClusterFixture fixture, AggregateSnapshotRaceRf3State state,
        Func<RequestCqrsRf3Callers, Task> action, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Scenario.WorkerSecret,
            async reader => { await action(reader); return true; }, token);
}
