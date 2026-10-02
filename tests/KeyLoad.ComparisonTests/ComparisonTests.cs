using System.Diagnostics;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests;

internal sealed class RealComparisonSuite
{
    private const string DocumentsKey = "Documents";
    private const string OperationsKey = "Operations";
    private const string WarmupKey = "Warmup";
    private const string RepetitionsKey = "Repetitions";
    private const string ConcurrencyKey = "Concurrency";
    private const string DimensionsKey = "Dimensions";
    private const string TopKKey = "TopK";

    [Test]
    public async Task AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-comparison-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "reports");
        var evidence = GetEvidenceDirectory();
        var options = ReadOptions();
        var neo4jPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        var builder = await CreateBuilderAsync(root, output, options, neo4jPassword, timeout.Token);
        ConfigureLogging(builder);
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var logCapture = new ComparisonTestLogCapture(app);
        try
        {
            await VerifyPinnedContainerImagesAsync(app);
            await app.StartAsync(timeout.Token);
            await VerifyCompletedRunAsync(app, output, evidence, options, neo4jPassword, timeout.Token);
        }
        finally
        {
            await logCapture.StopAsync();
            Directory.CreateDirectory(evidence);
            await logCapture.WriteToAsync(Path.Combine(evidence, "runner.log"));
            var redis = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
                .OfType<ContainerResource>().Single(resource => resource.Name == "benchmark-redis");
            await app.StopAsync(CancellationToken.None);
            await DeleteDataAsync(root, redis);
        }
    }

    private static ComparisonOptions ReadOptions()
    {
        var values = new Dictionary<string, string?>
        {
            [DocumentsKey] = "32",
            [OperationsKey] = "12",
            [WarmupKey] = "2",
            [RepetitionsKey] = "2",
            [ConcurrencyKey] = "2",
            [DimensionsKey] = "8",
            [TopKKey] = "3"
        };
        var options = new ConfigurationBuilder().AddInMemoryCollection(values)
            .AddEnvironmentVariables("KEYLOAD_COMPARISON_").Build().Get<ComparisonOptions>()!;
        options.Validate();
        return options;
    }

    private static string GetEvidenceDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx")))
        {
            repository = repository.Parent;
        }

        var reportName = Environment.GetEnvironmentVariable("KEYLOAD_COMPARISON_REPORT_NAME") ?? "smoke";
        return Path.Combine(repository.FullName, "artifacts", "comparisons", reportName);
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(string root, string output,
        ComparisonOptions options, string neo4jPassword, CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            $"--KeyLoad:DataRoot={Path.Combine(root, "cluster")}",
            "--KeyLoad:Ephemeral=true",
            "--Benchmarks:Enabled=true",
            $"--Parameters:benchmark-neo4j-password={neo4jPassword}",
            $"--Benchmarks:DataRoot={root}",
            $"--Benchmarks:Output={output}",
            $"--Benchmarks:{DocumentsKey}={options.Documents}",
            $"--Benchmarks:{OperationsKey}={options.Operations}",
            $"--Benchmarks:{WarmupKey}={options.Warmup}",
            $"--Benchmarks:{RepetitionsKey}={options.Repetitions}",
            $"--Benchmarks:{ConcurrencyKey}={options.Concurrency}",
            $"--Benchmarks:{DimensionsKey}={options.Dimensions}",
            $"--Benchmarks:{TopKKey}={options.TopK}",
            $"--Benchmarks:PayloadBytes={options.PayloadBytes}",
            $"--Benchmarks:Seed={options.Seed}",
            $"--Benchmarks:GraphVertices={options.GraphVertices}",
            $"--Benchmarks:GraphFanOut={options.GraphFanOut}",
            $"--Benchmarks:GraphDepth={options.GraphDepth}",
            $"--Benchmarks:TimeoutSeconds={options.TimeoutSeconds}",
            $"--Benchmarks:SourceRevision={Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unrecorded"}"
        };
        return DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, cancellationToken);
    }

    private static void ConfigureLogging(IDistributedApplicationTestingBuilder builder)
    {
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);
        });
    }

    private static async Task VerifyPinnedContainerImagesAsync(DistributedApplication app)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (var name in new[]
        {
            "benchmark-postgres-server", "benchmark-qdrant", "benchmark-rabbit", "benchmark-redis", "benchmark-neo4j"
        })
        {
            var container = model.Resources.OfType<ContainerResource>().Single(resource => resource.Name == name);
            await Assert.That(container.TryGetContainerImageName(out var image)).IsTrue();
            await Assert.That(image).Contains("@sha256:");
            await Assert.That(image).DoesNotContain("sha256:sha256:");
        }
    }

    private static async Task VerifyCompletedRunAsync(DistributedApplication app, string output, string evidence,
        ComparisonOptions options, string neo4jPassword, CancellationToken cancellationToken)
    {
        await app.ResourceNotifications.WaitForResourceAsync("comparisons",
            resource => resource.Snapshot.ExitCode is not null || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
            cancellationToken);
        await Assert.That(app.ResourceNotifications.TryGetCurrentState("comparisons", out var state)).IsTrue();
        CopyReportsIfPresent(output, evidence);
        await Assert.That(state!.Snapshot.ExitCode).IsEqualTo(0);
        var connectionString = await app.GetConnectionStringAsync("benchmark-postgres", cancellationToken)
            ?? throw new InvalidOperationException("Comparison PostgreSQL connection string is unavailable.");
        await PostgresSchemaRegression.VerifyAsync(connectionString, cancellationToken);
        var report = await ComparisonTestReportAssertions.VerifyAsync(output, options, cancellationToken);
        var neo4jImage = report.Targets.Single(target => target.Name == "Neo4j").Image
            ?? throw new InvalidOperationException("Neo4j image is missing from the comparison report.");
        var neo4jEndpoint = app.GetEndpoint("benchmark-neo4j", "http");
        await Neo4jHarnessMismatchRegression.VerifyAsync(neo4jEndpoint, neo4jPassword, neo4jImage, cancellationToken);
    }

    private static void CopyReportsIfPresent(string output, string evidence)
    {
        if (!File.Exists(Path.Combine(output, "results.json")))
        {
            return;
        }

        Directory.CreateDirectory(evidence);
        foreach (var file in Directory.EnumerateFiles(output))
        {
            File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), true);
        }
    }

    private static async Task DeleteDataAsync(string root, ContainerResource redis)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, true);
            return;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsLinux())
        {
        }

        var external = Path.Combine(root, "external");
        if (!Directory.Exists(external) || !redis.TryGetContainerImageName(out var image))
        {
            throw new IOException("Cannot clean the comparison run's container-owned data.");
        }

        var start = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[]
        {
            "run", "--rm", "--pull", "never", "--network", "none", "--read-only", "--user", "0:0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--entrypoint", "/bin/sh",
            "--mount", $"type=bind,source={external},target=/data", image!, "-c", "rm -rf /data/*"
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Cannot start comparison data cleanup.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await output;
        if (process.ExitCode != 0)
        {
            throw new IOException($"Comparison data cleanup failed: {await error}");
        }

        await error;
        Directory.Delete(root, true);
    }
}
