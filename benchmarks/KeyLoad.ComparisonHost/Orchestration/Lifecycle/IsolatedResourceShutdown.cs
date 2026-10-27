using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Joins the selected containers through Aspire before disposing their application owner.</summary>
internal static class IsolatedResourceShutdown
{
    private const string StopRejected = "SelectedDatabaseStopCommandRejected:";

    internal static Task StopAsync(DistributedApplication app, ContainerResource[] resources, CancellationToken token)
        => Task.WhenAll(resources.Select(resource => StopResourceAsync(app, resource.Name, token)));

    private static async Task StopResourceAsync(DistributedApplication app, string name, CancellationToken token)
    {
        if (!app.ResourceNotifications.TryGetCurrentState(name, out var current))
        {
            return;
        }
        var state = current.Snapshot.State?.Text;
        if (current.Snapshot.ExitCode is not null || state == KnownResourceStates.NotStarted
            || state == KnownResourceStates.Waiting || state == KnownResourceStates.Building
            || state == KnownResourceStates.FailedToStart)
        {
            return;
        }
        var stopped = await app.Services.GetRequiredService<ResourceCommandService>()
            .ExecuteCommandAsync(name, KnownResourceCommands.StopCommand, token);
        if (!stopped.Success)
        {
            throw new InvalidOperationException(StopRejected + name);
        }
        await app.ResourceNotifications.WaitForResourceAsync(name,
            static update => update.Snapshot.ExitCode is not null, token);
    }
}
