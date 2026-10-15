using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class MetamorphicPublicFlow
{
    internal static async Task RunAsync(int seed, bool descending, int offset)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                MetamorphicPublicState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => MetamorphicPublicSetup.RunAsync(fixture, root, seed, deadline.Token), deadline.Token);
                }
                var plans = Enumerable.Range(0, 4).Select(shape => MetamorphicPublicPlan.Create(
                    state.Partition, shape, descending, offset)).ToArray();
                await WarmAsync(fixture, state, plans, descending, offset);
                var coldFailures = new List<Exception>();
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, plans, descending, offset, false, deadline.Token);
                    }, coldFailures);
                }
                ServerFailureObserver.ThrowIfAny(coldFailures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, MetamorphicPublicState state,
        MetamorphicPublicPlan[] plans, bool descending, int offset)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await MetamorphicPublicOperations.RunAsync(root, reader, state, plans, descending, offset, true, deadline.Token);
                await MetamorphicPublicSetup.PolicyAsync(root, state, true, 3L, deadline.Token);
                await MetamorphicPublicOperations.RunAsync(root, reader, state, plans, descending, offset, false, deadline.Token);
                await MetamorphicPublicSetup.PolicyAsync(root, state, false, 4L, deadline.Token);
                await MetamorphicPublicOperations.RunAsync(root, reader, state, plans, descending, offset, true, deadline.Token);
                await MetamorphicPublicSetup.PolicyAsync(root, state, true, 5L, deadline.Token);
                await MetamorphicPublicOperations.RunAsync(root, reader, state, plans, descending, offset, false, deadline.Token);
                await MetamorphicPublicCurrent.ReplayAsync(root, state, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, MetamorphicPublicState state, MetamorphicPublicPlan[] plans,
        bool descending, int offset, bool denied, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await MetamorphicPublicOperations.RunAsync(root, reader, state, plans, descending, offset, denied, token);
                await MetamorphicPublicCurrent.ReplayAsync(root, state, token);
                return true;
            }, token), token);
}
