using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageClusterFlow
{
    internal static async Task RunAsync(bool expansion, bool emptyLabels)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                GraphSearchLineageClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => GraphSearchLineageClusterSetup.RunAsync(fixture, root, expansion, emptyLabels, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < GraphSearchLineageClusterProtocol.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
                    }, failures);
                    await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, false), failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, GraphSearchLineageClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
            {
                var failures = new List<Exception>();
                if (warm)
                {
                    await ServerFailureObserver.ObserveAsync(() => WarmAsync(fixture, root, caller, state, deadline.Token), failures);
                }
                await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
                    root, caller, state, state.Request, null, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.ReplayAsync(root, state, deadline.Token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }

    private static async Task WarmAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers caller, GraphSearchLineageClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
            root, caller, state, state.MissingRequest, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
            root, caller, state, state.Request, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterSetup.PolicyAsync(
            fixture, state, GraphSearchLineageClusterProtocol.UseEpoch, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
            root, caller, state, state.MissingRequest, ErrorCode.NotFound, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
            root, caller, state, state.Request, null, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterSetup.PolicyAsync(
            fixture, state, GraphSearchLineageClusterProtocol.WithdrawnEpoch, false, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterCalls.RunAsync(
            root, caller, state, state.Request, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageClusterSetup.PolicyAsync(
            fixture, state, GraphSearchLineageClusterProtocol.RepairedEpoch, true, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
