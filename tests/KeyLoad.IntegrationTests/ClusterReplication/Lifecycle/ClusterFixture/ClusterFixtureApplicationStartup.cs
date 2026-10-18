using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureApplicationStartup
{
    internal static ClusterFixtureColdStartObservation ObserveColdStart(string root)
        => new(root, Directory.Exists(root), File.Exists(root));

    internal static async Task<string> StartAsync(DistributedApplication application, IReadOnlyCollection<string> names,
        CancellationToken token)
    {
        await application.StartAsync(token).ConfigureAwait(false);
        await AspireStartupReadiness.WaitForHealthyAsync(application, names, token).ConfigureAwait(false);
        var output = new ClusterFailureReceipts().RequireRunOutput(TestContext.ResultsDirectory);
        Directory.CreateDirectory(output);
        _ = await CaptureAsync(application, names, output, "native-process-startup.json", token).ConfigureAwait(false);
        return output;
    }

    internal static async Task<NativeProcessEvidence[]> CaptureAsync(DistributedApplication application,
        IReadOnlyCollection<string> names, string output, string fileName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var observed = new List<NativeProcessEvidence>();
        foreach (var name in names)
        {
            observed.Add(await ReadAsync(application, name, token).ConfigureAwait(false));
        }
        await WriteAsync(output, fileName, observed, token).ConfigureAwait(false);
        return [.. observed];
    }

    internal static async Task<NativeProcessEvidence> ReadAsync(DistributedApplication application, string name,
        CancellationToken token)
    {
        return await ProjectProcessEvidence.ReadAsync(application.ResourceNotifications, name,
            application.Services.GetRequiredService<IOptions<TestExecutionOptions>>(), token)
            .ConfigureAwait(false);
    }

    internal static async Task WriteAsync<T>(string output, string fileName, T original, CancellationToken token)
    {
        await using var stream = new FileStream(Path.Combine(output, fileName), new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        });
        await JsonSerializer.SerializeAsync(stream, original, cancellationToken: token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}
