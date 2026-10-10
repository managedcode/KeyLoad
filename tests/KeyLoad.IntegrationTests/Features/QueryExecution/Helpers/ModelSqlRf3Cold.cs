using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelSqlRf3Cold
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var stopped = await Task.WhenAll(TopicSqlRf3Protocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, TopicSqlRf3Protocol.ColdScenario, token))));
        var started = await Task.WhenAll(TopicSqlRf3Protocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(value => value), .. started.SelectMany(value => value)]);
        foreach (var node in TopicSqlRf3Protocol.Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
