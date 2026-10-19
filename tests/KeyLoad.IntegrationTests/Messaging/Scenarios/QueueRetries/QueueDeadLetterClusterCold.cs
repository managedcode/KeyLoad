using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterClusterCold
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueDeadLetterClusterState state, NodeStatus before, CancellationToken token)
    {
        var killed = await Task.WhenAll(QueueDeadLetterClusterProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillServerAsync(node, QueueDeadLetterClusterProtocol.ColdScenario, token))));
        var restarted = await Task.WhenAll(QueueDeadLetterClusterProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartServerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. killed.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in QueueDeadLetterClusterProtocol.Nodes)
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token);
        }
        var failures = new List<Exception>();
        RequestCqrsClusterCallers? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fresh = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await fresh.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Position).IsGreaterThanOrEqualTo(before.Position);
            await QueueDeadLetterClusterAssertions.ReplayAsync(fresh, state, token);
            await QueueDeadLetterClusterScenario.HealthyAsync(fresh, state, QueueDeadLetterClusterProtocol.Second, token);
            await QueueDeadLetterClusterAssertions.LiteralAsync(fresh, state, token);
        }, failures);
        if (fresh is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => fresh.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
