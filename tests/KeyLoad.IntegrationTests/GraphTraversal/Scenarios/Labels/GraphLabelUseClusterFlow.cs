using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseClusterFlow
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
                GraphLabelUseClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await GraphLabelUseClusterSetup.CreateAsync(fixture, deadline.Token);
                }
                await WaveAsync(fixture, state, true);
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, GraphLabelUseClusterState state, bool authority)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var image = await GraphLabelUseClusterAssertions.ImageAsync(admin, state, deadline.Token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
                {
                    if (authority)
                    {
                        await GraphLabelUseClusterReads.RunAsync(reader, state, false, deadline.Token);
                        await GraphLabelUseClusterAuthority.RunAsync(admin, reader, state, deadline.Token);
                    }
                    await GraphLabelUseClusterReads.RunAsync(reader, state, true, deadline.Token);
                    return true;
                }, deadline.Token);
                await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.CommitAsync(state.Seed, deadline.Token)));
                await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, deadline.Token))).Value);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(await GraphLabelUseClusterAssertions.ImageAsync(admin, state, deadline.Token)).IsEqualTo(image);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, deadline.Token);
    }
}
