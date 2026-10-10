using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterRf3Cold
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDeadLetterRf3State state, NodeStatus before, CancellationToken token)
    {
        var killed = await Task.WhenAll(QueueDeadLetterRf3Protocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, QueueDeadLetterRf3Protocol.ColdScenario, token))));
        var restarted = await Task.WhenAll(QueueDeadLetterRf3Protocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. killed.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in QueueDeadLetterRf3Protocol.Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fresh = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await fresh.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
            await QueueDeadLetterRf3Assertions.ReplayAsync(fresh, state, token);
            await QueueDeadLetterRf3Scenario.HealthyAsync(fresh, state, QueueDeadLetterRf3Protocol.Second, token);
            await QueueDeadLetterRf3Assertions.LiteralAsync(fresh, state, token);
        }, failures);
        if (fresh is not null)
        { await ServerFailureObserver.ObserveAsync(() => fresh.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
