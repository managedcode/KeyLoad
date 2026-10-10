using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, int route, QueueParkedHeadPolicy parkedHead)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await SeedAsync(fixture, route, parkedHead, deadline.Token);
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? callers = null;
        NodeStatus? before = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.Caller.OperatorKey, deadline.Token);
            await QueueOrderedRetryPublicInitial.RunAsync(callers, state, deadline.Token);
            await DeniedAsync(fixture, state, deadline.Token);
            await QueueOrderedRetryPublicContinuation.RunAsync(callers, state, deadline.Token);
            before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
        }, failures);
        if (callers is not null)
        { await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await QueueOrderedRetryPublicCold.RunAsync(fixture, state, before!, healthy: true, deadline.Token);
        _ = await QueueOrderedRetryPublicCold.RunAsync(fixture, state, after, healthy: false, deadline.Token);
    }
    private static async Task<QueueOrderedRetryPublicState> SeedAsync(ClusterFixture fixture, int route, QueueParkedHeadPolicy parkedHead, CancellationToken token)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? root = null;
        QueueOrderedRetryPublicState? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            root = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            state = await QueueOrderedRetryPublicSeed.RunAsync(root, route, parkedHead, token);
        }, failures);
        if (root is not null)
        { await ServerFailureObserver.ObserveAsync(() => root.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return state!;
    }
    private static async Task DeniedAsync(ClusterFixture fixture, QueueOrderedRetryPublicState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? denied = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            denied = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.Caller.DeniedKey, token);
            var command = new CommandRequest(Guid.NewGuid(), state.Caller.Partition,
                [new RedriveQueueMessage(state.Caller.Lane.Queue, QueueOrderedRetryPublicProtocol.First,
                    QueueOrderedRetryPublicProtocol.Seven, QueueOrderedRetryPublicProtocol.One)]);
            await QueueLifecyclePublicRoutes.RefusedAsync(denied, state.Caller, command, ErrorCode.PermissionDenied, token);
        }, failures);
        if (denied is not null)
        { await ServerFailureObserver.ObserveAsync(() => denied.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
