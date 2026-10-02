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
        var evidence = ComparisonTestEvidenceFiles.GetDirectory();
        var options = ReadOptions();
        var neo4jPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        var builder = await CreateBuilderAsync(root, output, options, neo4jPassword, timeout.Token);
        ConfigureLogging(builder);
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var logCapture = new ComparisonTestLogCapture(app);
        await using var diagnostics = new ComparisonResourceDiagnostics(app.ResourceNotifications);
        diagnostics.Start();
        try
        {
            await ComparisonImageResourceAssertions.VerifyAsync(app, timeout.Token);
            await VerifyPinnedContainerImagesAsync(app);
            await app.StartAsync(timeout.Token);
            await VerifyCompletedRunAsync(app, output, evidence, options, neo4jPassword, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await diagnostics.WriteFailureAsync();
            throw;
        }
        finally
        {
            await diagnostics.DisposeAsync();
            await logCapture.StopAsync();
            Directory.CreateDirectory(evidence);
            await logCapture.WriteToAsync(Path.Combine(evidence, "runner.log"));
            var redis = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
                .OfType<ContainerResource>().Single(resource => resource.Name == "benchmark-redis");
            await app.StopAsync(CancellationToken.None);
            await RetainReportsAndDeleteDataAsync(root, redis, output, evidence);
        }
    }

    private static async Task RetainReportsAndDeleteDataAsync(string root, ContainerResource redis,
        string output, string evidence)
    {
        try
        {
            ComparisonTestEvidenceFiles.CopyReportsIfPresent(output, evidence);
        }
        finally
        {
            await ComparisonDataCleanup.DeleteDataAsync(root, redis);
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
        ComparisonTestEvidenceFiles.CopyReportsIfPresent(output, evidence);
        await Assert.That(state!.Snapshot.ExitCode).IsEqualTo(0);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var adminParameter = model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == "admin-key");
        var adminKey = await adminParameter.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException("Comparison KeyLoad credential is unavailable.");
        await KeyLoadStreamPublicRegression.VerifyAsync(app, adminKey, cancellationToken);
        var connectionString = await app.GetConnectionStringAsync("benchmark-postgres", cancellationToken)
            ?? throw new InvalidOperationException("Comparison PostgreSQL connection string is unavailable.");
        await PostgresSchemaRegression.VerifyAsync(connectionString, cancellationToken);
        var report = await ComparisonTestReportAssertions.VerifyAsync(output, options, cancellationToken);
        var neo4jImage = report.Targets.Single(target => target.Name == "Neo4j").Image
            ?? throw new InvalidOperationException("Neo4j image is missing from the comparison report.");
        var neo4jEndpoint = app.GetEndpoint("benchmark-neo4j", "http");
        await Neo4jHarnessMismatchRegression.VerifyAsync(neo4jEndpoint, neo4jPassword, neo4jImage, cancellationToken);
    }

}
