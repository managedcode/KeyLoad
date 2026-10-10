using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicCold
{
    internal static async Task<NodeStatus> RunAsync(ClusterFixture fixture, QueueOrderedRetryPublicState state,
        NodeStatus before, bool healthy, CancellationToken token)
    {
        var killed = await Task.WhenAll(QueueLifecyclePublicProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, QueueOrderedRetryPublicProtocol.ColdScenario, token))));
        var restarted = await Task.WhenAll(QueueLifecyclePublicProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. killed.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in QueueLifecyclePublicProtocol.Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? callers = null;
        NodeStatus? after = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.Caller.OperatorKey, token);
            after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
            await QueueOrderedRetryPublicTerminal.RunAsync(callers, state, healthy: !healthy, token);
            await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Caller, token);
            if (healthy)
            {
                await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Caller, new(Guid.NewGuid(), state.Caller.Partition,
                    [QueueOrderedRetryPublicOperations.Literal(QueueOrderedRetryPublicProtocol.Healthy)]), token);
                var delivery = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.Healthy, token);
                await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, delivery, DeliveryAction.Ack, token);
                await QueueOrderedRetryPublicTerminal.RunAsync(callers, state, healthy: true, token);
                await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Caller, token);
            }
            after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(token));
        }, failures);
        if (callers is not null)
        { await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return after!;
    }
    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
