using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Starts only selected database infrastructure, then runs the execution workload client outside Aspire's resource graph.</summary>
internal static class BenchmarkSessionClientRuntime
{
    private const int EmptyCount = 0;
    private const string TheSelectedDatabaseBootstrapProcessFailedMessage = "The selected database bootstrap process failed.";

    internal static async Task<int> RunAsync(DistributedApplication application, string evidence, CancellationToken token)
    {
        await application.StartAsync(token);
        var settings = await WaitForReadinessAsync(application, token);
        var environment = (await settings.ResolveAsync(token)).ToDictionary(StringComparer.Ordinal);
        var policy = application.Services.GetRequiredService<IOptions<TestExecutionOptions>>();
        return await IsolatedClientExecution.RunAsync(application, settings.Resources,
            lifetime => BenchmarkSessionClientProcess.RunAsync(environment, evidence, policy, lifetime), token);
    }

    internal static async Task<LoadTestClientSettings> WaitForReadinessAsync(DistributedApplication application, CancellationToken token)
    {
        var settings = application.Services.GetRequiredService<LoadTestClientSettings>();
        foreach (var resource in settings.Resources)
        {
            await application.ResourceNotifications.WaitForResourceHealthyAsync(resource, WaitBehavior.StopOnResourceUnavailable, token);
        }
        foreach (var resource in settings.CompletionResources)
        {
            var completed = await application.ResourceNotifications.WaitForResourceAsync(resource,
                update => AspireTerminalResource.IsTerminal(update.Snapshot), token);
            if (completed.Snapshot.ExitCode != EmptyCount)
            {
                throw new InvalidOperationException(TheSelectedDatabaseBootstrapProcessFailedMessage);
            }
        }
        return settings;
    }
}
