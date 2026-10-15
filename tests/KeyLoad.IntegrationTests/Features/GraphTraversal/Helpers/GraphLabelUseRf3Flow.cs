using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseRf3Flow
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
                GraphLabelUseRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await GraphLabelUseRf3Setup.CreateAsync(fixture, deadline.Token);
                }
                await WaveAsync(fixture, state, true);
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, GraphLabelUseRf3State state, bool authority)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var image = await GraphLabelUseRf3Assertions.ImageAsync(admin, state, deadline.Token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
                {
                    if (authority)
                    {
                        await GraphLabelUseRf3Reads.RunAsync(reader, state, false, deadline.Token);
                        await GraphLabelUseRf3Authority.RunAsync(admin, reader, state, deadline.Token);
                    }
                    await GraphLabelUseRf3Reads.RunAsync(reader, state, true, deadline.Token);
                    return true;
                }, deadline.Token);
                await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.CommitAsync(state.Seed, deadline.Token)));
                await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, deadline.Token))).Value);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(await GraphLabelUseRf3Assertions.ImageAsync(admin, state, deadline.Token)).IsEqualTo(image);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, deadline.Token);
    }
}
