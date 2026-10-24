using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Comparisons;
using KeyLoad.Diagnostics.ResourceExecution;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Starts only selected database infrastructure, then runs the execution workload client outside Aspire's resource graph.</summary>
internal static class BenchmarkSessionClientRuntime
{
    private const int EmptyCount = 0;
    private const string TheSelectedDatabaseBootstrapProcessFailedMessage = "The selected database bootstrap process failed.";
    private const string TheSelectedDatabaseDoesNotExposeTheRequiredExecutionMessage = "The selected database does not expose the required execution server processes.";

    private const string KeyLoad = "KeyLoad";
    private const string ServerProcesses = "Benchmarks__Execution__Processes";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<int> RunAsync(DistributedApplication application, BenchmarkSessionSelection selection,
        string evidence, CancellationToken token)
    {
        var settings = application.Services.GetRequiredService<LoadTestClientSettings>();
        await application.StartAsync(token);
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
        var environment = (await settings.ResolveAsync(token)).ToDictionary(StringComparer.Ordinal);
        if (selection.Target == KeyLoad)
        {
            var model = application.Services.GetRequiredService<DistributedApplicationModel>();
            var processes = new List<ProcessEvidence>();
            foreach (var resource in model.Resources.OfType<ProjectResource>())
            {
                processes.Add(await ProjectProcessEvidence.ReadAsync(application.ResourceNotifications,
                    resource.Name, application.Services.GetRequiredService<IOptions<TestExecutionOptions>>(), token));
            }
            if (processes.Count != selection.NodeCount)
            {
                throw new InvalidDataException(TheSelectedDatabaseDoesNotExposeTheRequiredExecutionMessage);
            }
            environment.Add(ServerProcesses, JsonSerializer.Serialize(processes, Json));
        }
        var policy = application.Services.GetRequiredService<IOptions<TestExecutionOptions>>();
        return await IsolatedClientExecution.RunAsync(application, settings.Resources,
            lifetime => BenchmarkSessionClientProcess.RunAsync(environment, evidence, policy, lifetime), token);
    }
}
