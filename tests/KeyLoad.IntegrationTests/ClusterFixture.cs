using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests;

public sealed class ClusterFixture : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-cluster-" + Guid.NewGuid().ToString("N"));
    public DistributedApplication App { get; private set; } = null!;
    public string AdminKey { get; private set; } = "";
    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
            [$"--KeyLoad:DataRoot={Root}", "--KeyLoad:Ephemeral=true"], timeout.Token);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders(); logging.AddConsole(); logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);
        });
        App = await builder.BuildAsync(timeout.Token);
        await App.StartAsync(timeout.Token);
        using var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "local-profile.json")));
        AdminKey = profile.RootElement.GetProperty("AdminKey").GetString()!;
        await Task.WhenAll(Enumerable.Range(1, 3).Select(i => App.ResourceNotifications.WaitForResourceHealthyAsync($"node{i}", timeout.Token)));
    }
    public KeyLoadClient Client(string node, string? key = null)
    {
        var http = App.CreateHttpClient(node, "http"); http.Timeout = TimeSpan.FromSeconds(30);
        return new(http, key ?? AdminKey);
    }
    public async ValueTask DisposeAsync()
    {
        if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
[CollectionDefinition("rf3", DisableParallelization = true)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>;
