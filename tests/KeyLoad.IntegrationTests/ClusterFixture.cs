using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using System.Collections.Concurrent;
using KeyLoad.Client;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests;

public sealed class ClusterFixture : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-cluster-" + Guid.NewGuid().ToString("N"));
    public DistributedApplication App { get; private set; } = null!;
    public string AdminKey { get; private set; } = "";
    private byte[] peerSecret = [];
    private readonly CancellationTokenSource loggingLifetime = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> nodeLogs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> logCapture = new(StringComparer.Ordinal);
    private Task? resourceCapture;
    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
            [$"--KeyLoad:DataRoot={Root}", "--KeyLoad:Ephemeral=true", "--KeyLoad:SnapshotThreshold=16"], timeout.Token);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders(); logging.AddConsole(); logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);
        });
        App = await builder.BuildAsync(timeout.Token);
        var logs = App.Services.GetRequiredService<ResourceLoggerService>();
        resourceCapture = CaptureResourcesAsync(logs);
        await App.StartAsync(timeout.Token);
        using var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "local-profile.json")));
        AdminKey = profile.RootElement.GetProperty("AdminKey").GetString()!;
        peerSecret = Convert.FromBase64String(profile.RootElement.GetProperty("PeerSecret").GetString()!);
        try { await Task.WhenAll(Enumerable.Range(1, 3).Select(i => App.ResourceNotifications.WaitForResourceHealthyAsync($"node{i}", timeout.Token))); }
        catch { await SaveFailureDiagnosticsAsync(); throw; }
    }
    public KeyLoadClient Client(string node, string? key = null)
    {
        var http = App.CreateHttpClient(node, "http"); http.Timeout = TimeSpan.FromSeconds(30);
        return new(http, key ?? AdminKey);
    }
    private async Task CaptureResourcesAsync(ResourceLoggerService logs)
    {
        try
        {
            await foreach (var change in App.ResourceNotifications.WatchAsync(loggingLifetime.Token))
                if (change.Resource.Name is "node1" or "node2" or "node3")
                    _ = logCapture.GetOrAdd(change.ResourceId, id => CaptureLogsAsync(logs, id, change.Resource.Name));
        }
        catch (OperationCanceledException) when (loggingLifetime.IsCancellationRequested) { }
    }
    private async Task CaptureLogsAsync(ResourceLoggerService logs, string resourceId, string name)
    {
        var buffer = nodeLogs.GetOrAdd(name, _ => new());
        try
        {
            // A live subscription starts DCP's log forwarding even when no dashboard is connected.
            await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(loggingLifetime.Token))
                foreach (var line in batch)
                {
                    buffer.Enqueue(line.Content);
                    while (buffer.Count > 120) buffer.TryDequeue(out _);
                }
        }
        catch (OperationCanceledException) when (loggingLifetime.IsCancellationRequested) { }
    }
    public async Task SaveFailureDiagnosticsAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx"))) repository = repository.Parent;
        var output = Path.Combine(repository.FullName, "artifacts", "qualification"); Directory.CreateDirectory(output);
        foreach (var number in Enumerable.Range(1, 3))
        {
            var name = $"node{number}";
            var tail = nodeLogs.TryGetValue(name, out var buffer) ? buffer.ToArray() : [];
            var state = App.ResourceNotifications.TryGetCurrentState(name, out var current)
                ? $"Resource {current.ResourceId}, state {current.Snapshot.State?.Text}, exit {current.Snapshot.ExitCode}, health {current.Snapshot.HealthStatus}" : "Resource state unavailable";
            var consensus = "Consensus diagnostics unavailable";
            try
            {
                using var http = new HttpClient(new PeerSecurity(peerSecret).CreateHandler()) { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await http.GetAsync(new Uri(App.GetEndpoint(name, "http"), "/internal/state"));
                if (response.IsSuccessStatusCode) consensus = await response.Content.ReadAsStringAsync();
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
            File.WriteAllLines(Path.Combine(output, "rf3-failure-" + name + ".log"), new[] { state, consensus }.Concat(tail));
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {consensus}");
            foreach (var line in tail) TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {line}");
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await SaveFailureDiagnosticsAsync();
            await loggingLifetime.CancelAsync();
            if (resourceCapture is not null) await resourceCapture;
            await Task.WhenAll(logCapture.Values);
            await App.StopAsync(); await App.DisposeAsync();
        }
        loggingLifetime.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
[CollectionDefinition("rf3", DisableParallelization = true)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>;
