using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlParserAdmissionClusterFlow
{
    private const int ColdCycles = 2;
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            var retention = ClusterFixtureFailureRootRetention.For(fixture);
            retention.Enable();
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(fixture), failures);
            if (failures.Count != 0)
            {
                retention.Retain();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task BodyAsync(ClusterFixture fixture)
    {
        await fixture.InitializeAsync();
        SqlParserAdmissionClusterState state;
        using (var deadline = McpCallerDeadline.Create())
        {
            state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                root => SqlParserAdmissionClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
        }
        await CallersAsync(fixture, state, true);
        for (var cold = 0; cold < ColdCycles; cold++)
        {
            using var deadline = McpCallerDeadline.Create();
            await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
            await CallersAsync(fixture, state, false);
        }
    }
    private static async Task CallersAsync(ClusterFixture fixture, SqlParserAdmissionClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Original.Identity.Secret, async reader =>
            {
                if (warm)
                {
                    await SqlParserAdmissionClusterWave.WarmAsync(root, reader, state, deadline.Token);
                }
                else
                {
                    await SqlParserAdmissionClusterWave.RunAsync(root, reader, state, deadline.Token);
                }
                return true;
            }, deadline.Token), deadline.Token);
    }
}
