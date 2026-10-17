using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputRf3Flow
{
    internal static async Task RunAsync(int mode)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new DatabaseLimits
            {
                MaxBatchBytes = CapabilityOutputRf3Protocol.NativeBytes,
                MaxQueryBytes = CapabilityOutputRf3Protocol.RequestBytes
            }, CapabilityOutputRf3Protocol.Options(mode));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                CapabilityOutputRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await CapabilityOutputRf3Setup.CreateAsync(fixture, deadline.Token);
                }
                await WaveAsync(fixture, state, mode, true);
                for (var cold = 0; cold < 2; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, mode, false);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, CapabilityOutputRf3State state, int mode, bool authority)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var image = await CapabilityOutputRf3Assertions.ImageAsync(root.Sdk, state, deadline.Token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async caller =>
                {
                    await CapabilityOutputRf3Pages.RunAsync(caller, state, mode, deadline.Token);
                    if (authority)
                    {
                        await CapabilityOutputRf3Authority.RunAsync(root.Sdk, caller, state, mode, deadline.Token);
                    }
                    return true;
                }, deadline.Token);
                foreach (var seed in state.Seeds)
                {
                    var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed.Command, deadline.Token));
                    await SqlRf3Protocol.EqualAsync(seed.Receipt, actual);
                    var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                        await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Command, deadline.Token))).Value;
                    await SqlRf3Protocol.EqualAsync(seed.Receipt, official);
                }
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(await CapabilityOutputRf3Assertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, deadline.Token);
    }
}
