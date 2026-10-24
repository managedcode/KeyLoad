using Aspire.Hosting;
using KeyLoad.AppHost.TestInfrastructure;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Joins the original execution workload and selected database-state observer on every completion path.</summary>
internal static class IsolatedClientExecution
{
    internal static async Task<int> RunAsync(DistributedApplication application, IReadOnlyCollection<string> resources,
        Func<CancellationToken, Task<int>> operation, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var client = operation(lifetime.Token);
        var observation = ObserveAsync(application, resources, lifetime.Token);
        var primary = await Task.WhenAny(client, observation);
        await AspireOwnedTaskJoin.CompleteAsync(lifetime, [client, observation], primary);
        return await client;
    }

    private static async Task ObserveAsync(DistributedApplication application, IReadOnlyCollection<string> resources,
        CancellationToken token)
    {
        await foreach (var update in application.ResourceNotifications.WatchAsync(token))
        {
            if (resources.Contains(update.Resource.Name))
            {
                AspireTerminalResource.ValidateDependency(update.Resource.Name, update.Snapshot, null);
            }
        }
        token.ThrowIfCancellationRequested();
        throw new OperationCanceledException(token);
    }
}
