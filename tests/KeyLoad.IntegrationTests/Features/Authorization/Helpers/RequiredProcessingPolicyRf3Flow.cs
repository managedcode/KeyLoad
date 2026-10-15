using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Flow
{
    internal static async Task RunAsync(bool header)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                RequiredProcessingPolicyRf3State? state = null;
                using (var deadline = McpCallerDeadline.Create())
                {
                    await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
                    {
                        state = await RequiredProcessingPolicyRf3Setup.RunAsync(root, header, deadline.Token);
                        return true;
                    }, deadline.Token);
                }
                await WaveAsync(fixture, state!, true);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state!, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, RequiredProcessingPolicyRf3State state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Key, async reader =>
            {
                if (warm)
                {
                    await RequiredProcessingPolicyRf3Warm.RunAsync(root, reader, state, deadline.Token);
                }
                else
                {
                    await RequiredProcessingPolicyRf3Final.RunAsync(root, reader, state, deadline.Token);
                }
                return true;
            }, deadline.Token);
            return true;
        }, deadline.Token);
    }
}
