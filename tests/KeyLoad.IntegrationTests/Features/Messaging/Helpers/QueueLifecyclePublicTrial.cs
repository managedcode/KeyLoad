using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, int route)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await SeedAsync(fixture, route, deadline.Token);
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? callers = null;
        NodeStatus? before = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.OperatorKey, deadline.Token);
            await QueueLifecyclePublicPhase.BeginAsync(callers, state, deadline.Token);
            await DeniedAsync(fixture, state, deadline.Token);
            await QueueLifecyclePublicAssertions.InitialAsync(callers, state, deadline.Token);
            before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
        }, failures);
        if (callers is not null)
        { await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await QueueLifecyclePublicCold.RunAsync(fixture, state, before!, continueOperation: true, deadline.Token);
        _ = await QueueLifecyclePublicCold.RunAsync(fixture, state, after, continueOperation: false, deadline.Token);
    }

    private static async Task<QueueLifecyclePublicState> SeedAsync(ClusterFixture fixture, int route, CancellationToken token)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? root = null;
        QueueLifecyclePublicState? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            root = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            state = await QueueLifecyclePublicSeed.RunAsync(root, route, token);
        }, failures);
        if (root is not null)
        { await ServerFailureObserver.ObserveAsync(() => root.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return state!;
    }

    private static async Task DeniedAsync(ClusterFixture fixture, QueueLifecyclePublicState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? denied = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            denied = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.DeniedKey, token);
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new CancelQueueMessage(state.Lane.Queue, QueueLifecyclePublicProtocol.Parked, QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.One)]);
            await QueueLifecyclePublicRoutes.RefusedAsync(denied, state, command, ErrorCode.PermissionDenied, token);
        }, failures);
        if (denied is not null)
        { await ServerFailureObserver.ObserveAsync(() => denied.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
