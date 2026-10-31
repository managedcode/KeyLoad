using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>Starts the selected database through the load-test AppHost and reads its published connection.</summary>
internal static class BenchmarkApplication
{
    private const string DatabaseArgument = "--Benchmarks:Database=";
    private const string NodesArgument = "--Benchmarks:Nodes=";
    private const string ImageArgument = "--Benchmarks:KeyLoadImage=";
    private const string DataRootArgument = "--Benchmarks:DataRoot=";
    private const string DataDirectory = "benchmark-data";
    private const string BootstrapFailed = "The database bootstrap container exited with code ";
    private const string ContainerLogs = "KeyLoad.LoadTests.AppHost.Resources";

    internal static async Task<DistributedApplication> StartAsync(BenchmarkJob job, CancellationToken token)
    {
        var dataRoot = Path.Combine(AppContext.BaseDirectory, DataDirectory, Guid.NewGuid().ToString("N"));
        string[] arguments = [DatabaseArgument + job.Database, NodesArgument + job.Nodes.ToString(CultureInfo.InvariantCulture),
            ImageArgument + job.KeyLoadImage, DataRootArgument + dataRoot];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_LoadTests_AppHost>(arguments, token);
        // Containers write ordinary startup output to stderr, which Aspire logs as errors; their own logs are not job output.
        builder.Services.AddLogging(logging => logging.ClearProviders().AddSimpleConsole().SetMinimumLevel(LogLevel.Warning)
            .AddFilter(ContainerLogs, LogLevel.None));
        var application = await builder.BuildAsync(token);
        try
        {
            await application.StartAsync(token);
            return application;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            await application.DisposeAsync();
            throw;
        }
    }

    /// <summary>Waits until every published node is healthy and every bootstrap container finished, then reads the settings.</summary>
    internal static async Task<BenchmarkConnection> ConnectAsync(DistributedApplication application, BenchmarkJob job,
        CancellationToken token)
    {
        var settings = application.Services.GetRequiredService<LoadTestClientSettings>();
        foreach (var resource in settings.Resources)
        {
            await application.ResourceNotifications.WaitForResourceHealthyAsync(resource, WaitBehavior.StopOnResourceUnavailable, token);
        }
        foreach (var resource in settings.CompletionResources)
        {
            var completed = await application.ResourceNotifications.WaitForResourceAsync(resource,
                update => KnownResourceStates.TerminalStates.Contains(update.Snapshot.State?.Text), token);
            if (completed.Snapshot.ExitCode is not 0)
            {
                throw new InvalidOperationException(BootstrapFailed + completed.Snapshot.ExitCode);
            }
        }
        return BenchmarkConnection.Read(await settings.ResolveAsync(token), job);
    }
}
