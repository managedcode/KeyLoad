using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class CapabilityOutputClusterFlow
{
    internal static async Task RunAsync(int mode)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(CapabilityOutputClusterProtocol.Options(mode));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                CapabilityOutputClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await CapabilityOutputClusterSetup.CreateAsync(fixture, deadline.Token);
                }
                await WaveAsync(fixture, state, mode, true);
                for (var cold = 0; cold < 2; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, mode, false);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, CapabilityOutputClusterState state, int mode, bool authority)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var image = await CapabilityOutputClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async caller =>
                {
                    await CapabilityOutputClusterPages.RunAsync(caller, state, mode, deadline.Token);
                    if (authority)
                    {
                        await CapabilityOutputClusterAuthority.RunAsync(root.Sdk, caller, state, mode, deadline.Token);
                    }
                    return true;
                }, deadline.Token);
                foreach (var seed in state.Seeds)
                {
                    var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed.Command, deadline.Token));
                    await SqlClusterProtocol.EqualAsync(seed.Receipt, actual);
                    var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                        await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Command, deadline.Token))).Value;
                    await SqlClusterProtocol.EqualAsync(seed.Receipt, official);
                }
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(await CapabilityOutputClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, deadline.Token);
    }
}
