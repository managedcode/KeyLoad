using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Starts only selected database infrastructure, then runs the native workload client outside Aspire's resource graph.</summary>
internal static class BenchmarkSessionClientRuntime
{
    private const string KeyLoad = "KeyLoad";
    private const string ServerProcesses = "Benchmarks__Native__Processes";
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
            if (completed.Snapshot.ExitCode != 0)
            {
                throw new InvalidOperationException("The selected database bootstrap process failed.");
            }
        }
        var environment = (await settings.ResolveAsync(token)).ToDictionary(StringComparer.Ordinal);
        if (selection.Target == KeyLoad)
        {
            var model = application.Services.GetRequiredService<DistributedApplicationModel>();
            var processes = model.Resources.OfType<ProjectResource>().Select(resource =>
                ProjectProcessEvidence.Read(application.ResourceNotifications, resource.Name)).ToArray();
            if (processes.Length != selection.NodeCount)
            {
                throw new InvalidDataException("The selected database does not expose the required native server processes.");
            }
            environment.Add(ServerProcesses, JsonSerializer.Serialize(processes, Json));
        }
        var policy = application.Services.GetRequiredService<IOptions<TestExecutionOptions>>();
        return await IsolatedNativeClientExecution.RunAsync(application, settings.Resources,
            lifetime => BenchmarkSessionClientProcess.RunAsync(environment, evidence, policy, lifetime), token);
    }
}
