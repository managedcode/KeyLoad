using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                TimeSeriesChunkQualificationClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => TimeSeriesChunkQualificationClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WarmAsync(fixture, state), failures);
                for (var cold = 0; cold < TimeSeriesChunkQualificationClusterData.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, TimeSeriesChunkQualificationClusterState state)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterLifecycle.AuthorityAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterLifecycle.RetainAsync(fixture, state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task WaveAsync(ClusterFixture fixture, TimeSeriesChunkQualificationClusterState state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await TimeSeriesChunkQualificationClusterCurrent.ObserveAsync(root, state, async () =>
                {
                    await TimeSeriesChunkQualificationClusterRefusals.CancellationAsync(reader, state);
                    await TimeSeriesChunkQualificationClusterRefusals.BoundAsync(reader, state, deadline.Token);
                    await TimeSeriesChunkQualificationClusterReads.RunAsync(reader, state, true, deadline.Token);
                }, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
}
