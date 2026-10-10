using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicCold
{
    internal static async Task<NodeStatus> RunAsync(ClusterFixture fixture, QueueLifecyclePublicState state,
        NodeStatus before, bool continueOperation, CancellationToken token)
    {
        var killed = await Task.WhenAll(QueueLifecyclePublicProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, QueueLifecyclePublicProtocol.ColdScenario, token))));
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
            callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, state.OperatorKey, token);
            after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
            if (continueOperation)
            { await QueueLifecyclePublicPhase.ContinueAsync(fixture, callers, state, token); }
            else
            { await FinalAsync(callers, state, token); }
            after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(token));
        }, failures);
        if (callers is not null)
        { await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return after!;
    }

    private static async Task FinalAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.TerminalAsync(callers, state, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Acked, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Four, null, null, LeaseVersion: QueueLifecyclePublicProtocol.One), false, token);
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state, token);
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecyclePublicProtocol.LeaseSeconds);
            var result = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Partition, McpCallerTools.MessagesReceive,
                request, () => callers.Sdk.ReceiveAsync(request, token), token);
            await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
            await Assert.That(result.Deliveries).IsEmpty();
        }
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
