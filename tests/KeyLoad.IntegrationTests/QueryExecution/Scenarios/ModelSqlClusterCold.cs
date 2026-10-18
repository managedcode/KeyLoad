using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelSqlClusterCold
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var owner = ModelSqlClusterColdOwnership.Require(fixture);
        await owner.RequirePreviousAsync(token);
        var scenario = owner.Claim(fixture);
        var stopped = await Task.WhenAll(TopicSqlClusterProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillServerAsync(node, scenario, token))));
        var started = await Task.WhenAll(TopicSqlClusterProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartServerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(value => value), .. started.SelectMany(value => value)]);
        foreach (var node in TopicSqlClusterProtocol.Nodes)
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token);
        }
        await owner.CaptureAsync(scenario, token);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}
