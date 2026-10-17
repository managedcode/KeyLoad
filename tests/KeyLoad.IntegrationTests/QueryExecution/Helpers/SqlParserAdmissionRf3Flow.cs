using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3Flow
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
        SqlParserAdmissionRf3State state;
        using (var deadline = McpCallerDeadline.Create())
        {
            state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                root => SqlParserAdmissionRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
        }
        await CallersAsync(fixture, state, true);
        for (var cold = 0; cold < ColdCycles; cold++)
        {
            using var deadline = McpCallerDeadline.Create();
            await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
            await CallersAsync(fixture, state, false);
        }
    }
    private static async Task CallersAsync(ClusterFixture fixture, SqlParserAdmissionRf3State state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Original.Identity.Secret, async reader =>
            {
                if (warm)
                {
                    await SqlParserAdmissionRf3Wave.WarmAsync(root, reader, state, deadline.Token);
                }
                else
                {
                    await SqlParserAdmissionRf3Wave.RunAsync(root, reader, state, deadline.Token);
                }
                return true;
            }, deadline.Token), deadline.Token);
    }
}
