using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Flow
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
                ModelHeadRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => ModelHeadRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await WarmAsync(fixture, state);
                var coldFailures = new List<Exception>();
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, deadline.Token);
                    }, coldFailures);
                }
                ServerFailureObserver.ThrowIfAny(coldFailures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, ModelHeadRf3State state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await ModelHeadRf3Operations.RunAsync(root, reader, state, true, deadline.Token);
                await ModelHeadRf3Setup.PolicyAsync(root, state, true, 3L, deadline.Token);
                await ModelHeadRf3Operations.RunAsync(root, reader, state, false, deadline.Token);
                await ModelHeadRf3Setup.PolicyAsync(root, state, false, 4L, deadline.Token);
                await ModelHeadRf3Operations.RunAsync(root, reader, state, true, deadline.Token);
                await ModelHeadRf3Setup.PolicyAsync(root, state, true, 5L, deadline.Token);
                await ModelHeadRf3Operations.RunAsync(root, reader, state, false, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, ModelHeadRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await ModelHeadRf3Operations.RunAsync(root, reader, state, false, token);
                return true;
            }, token), token);
}
