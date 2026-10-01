using System.Collections.Concurrent;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests;

public sealed class ComparisonTests
{
    [Fact]
    public async Task AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-comparison-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "reports");
        var options = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Documents"] = "32", ["Operations"] = "12", ["Warmup"] = "2", ["Repetitions"] = "2",
            ["Concurrency"] = "2", ["Dimensions"] = "8", ["TopK"] = "3"
        }).AddEnvironmentVariables("KEYLOAD_COMPARISON_").Build().Get<ComparisonOptions>()!;
        options.Validate();
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx"))) repository = repository.Parent;
        var evidence = Path.Combine(repository.FullName, "artifacts", "comparisons", Environment.GetEnvironmentVariable("KEYLOAD_COMPARISON_REPORT_NAME") ?? "smoke");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>([
            $"--KeyLoad:DataRoot={Path.Combine(root, "cluster")}", "--KeyLoad:Ephemeral=true", "--Benchmarks:Enabled=true",
            $"--Benchmarks:DataRoot={root}",
            $"--Benchmarks:Output={output}", $"--Benchmarks:Documents={options.Documents}", $"--Benchmarks:Operations={options.Operations}", $"--Benchmarks:Warmup={options.Warmup}",
            $"--Benchmarks:Repetitions={options.Repetitions}", $"--Benchmarks:Concurrency={options.Concurrency}", $"--Benchmarks:Dimensions={options.Dimensions}",
            $"--Benchmarks:TopK={options.TopK}", $"--Benchmarks:PayloadBytes={options.PayloadBytes}", $"--Benchmarks:Seed={options.Seed}",
            $"--Benchmarks:TimeoutSeconds={options.TimeoutSeconds}"], timeout.Token);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders(); logging.AddConsole(); logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);
        });
        await using var app = await builder.BuildAsync(timeout.Token);
        var lines = new ConcurrentQueue<string>();
        using var captureLifetime = new CancellationTokenSource();
        var logger = app.Services.GetRequiredService<ResourceLoggerService>();
        var logCaptures = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);
        async Task CaptureLogsAsync(string resourceId)
        {
            try
            {
                await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(captureLifetime.Token))
                    foreach (var line in batch) { lines.Enqueue(line.Content); while (lines.Count > 150) lines.TryDequeue(out _); }
            }
            catch (OperationCanceledException) when (captureLifetime.IsCancellationRequested) { }
        }
        var capture = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in app.ResourceNotifications.WatchAsync(captureLifetime.Token))
                    if (change.Resource.Name == "comparisons")
                        _ = logCaptures.GetOrAdd(change.ResourceId, CaptureLogsAsync);
            }
            catch (OperationCanceledException) when (captureLifetime.IsCancellationRequested) { }
        }, CancellationToken.None);
        try
        {
            var model = app.Services.GetRequiredService<DistributedApplicationModel>();
            foreach (var container in model.Resources.OfType<ContainerResource>())
            {
                Assert.True(container.TryGetContainerImageName(out var image));
                Assert.Contains("@sha256:", image); Assert.DoesNotContain("sha256:sha256:", image);
            }
            await app.StartAsync(timeout.Token);
            // The runner's WaitFor edges enforce readiness. ExitCode handles both Finished and Exited states.
            await app.ResourceNotifications.WaitForResourceAsync("comparisons",
                resource => resource.Snapshot.ExitCode is not null || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart, timeout.Token);
            Assert.True(app.ResourceNotifications.TryGetCurrentState("comparisons", out var state));
            if (File.Exists(Path.Combine(output, "results.json")))
            {
                Directory.CreateDirectory(evidence);
                foreach (var file in Directory.EnumerateFiles(output)) File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), true);
            }
            Assert.Equal(0, state.Snapshot.ExitCode);
            var report = JsonSerializer.Deserialize<ComparisonReport>(File.ReadAllText(Path.Combine(output, "results.json")), ReportWriter.JsonOptions)!;
            Assert.Equal(5, report.Targets.Length); Assert.Equal(20 * options.Repetitions, report.Cases.Length);
            Assert.DoesNotContain(report.Cases, item => item.Status == "failed");
            Assert.Equal(12 * options.Repetitions, report.Cases.Count(item => item.Status == "measured"));
            Assert.All(report.Cases.Where(item => item.Status == "measured"), item =>
            {
                Assert.Equal(options.Operations, item.Measurement!.Successes);
                if (item.Scenario == Scenario.QueueCycle)
                {
                    Assert.Equal(options.Operations, item.Measurement.UniqueCompletedMessages);
                    Assert.NotNull(item.Measurement.Enqueue); Assert.NotNull(item.Measurement.Receive); Assert.NotNull(item.Measurement.Ack);
                }
            });
            Assert.Contains(report.Targets, target => target.Name == "KeyLoad" && target.Topology.Contains("RF3", StringComparison.Ordinal));
            Assert.All(report.Targets.Where(target => target.Name != "KeyLoad"), target => Assert.Contains("@sha256:", target.Image));
            Assert.Equal(1 + 12 * options.Repetitions * options.Operations, File.ReadAllLines(Path.Combine(output, "samples.csv")).Length);
        }
        finally
        {
            await captureLifetime.CancelAsync(); await capture; await Task.WhenAll(logCaptures.Values);
            Directory.CreateDirectory(evidence);
            await File.WriteAllLinesAsync(Path.Combine(evidence, "runner.log"), lines, CancellationToken.None);
            await app.StopAsync(CancellationToken.None);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
