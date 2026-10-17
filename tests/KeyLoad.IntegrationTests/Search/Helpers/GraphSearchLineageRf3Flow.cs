using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Flow
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
                fixture.RegisterNativeCoverageCase<GraphSearchLineageRf3Tests>(
                    nameof(GraphSearchLineageRf3Tests.AllRequestedGraphLabelsUseFreshAuthorityBeforeEarlierLookupAndRecoverExactResultsAcrossTwoCold));
                GraphSearchLineageRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => GraphSearchLineageRf3Setup.RunAsync(fixture, root, expansion, emptyLabels, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < GraphSearchLineageRf3Protocol.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                    }, failures);
                    await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, false), failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, GraphSearchLineageRf3State state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
            {
                var failures = new List<Exception>();
                if (warm)
                {
                    await ServerFailureObserver.ObserveAsync(() => WarmAsync(fixture, root, caller, state, deadline.Token), failures);
                }
                await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
                    root, caller, state, state.Request, null, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.ReplayAsync(root, state, deadline.Token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }

    private static async Task WarmAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers caller, GraphSearchLineageRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
            root, caller, state, state.MissingRequest, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
            root, caller, state, state.Request, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Setup.PolicyAsync(
            fixture, state, GraphSearchLineageRf3Protocol.UseEpoch, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
            root, caller, state, state.MissingRequest, ErrorCode.NotFound, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
            root, caller, state, state.Request, null, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Setup.PolicyAsync(
            fixture, state, GraphSearchLineageRf3Protocol.WithdrawnEpoch, false, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Calls.RunAsync(
            root, caller, state, state.Request, ErrorCode.PermissionDenied, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageRf3Setup.PolicyAsync(
            fixture, state, GraphSearchLineageRf3Protocol.RepairedEpoch, true, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
