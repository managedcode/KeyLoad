using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesAspireProfileTests
{
    private const string TimescaleServerResource = "benchmark-timescale-server";
    private const string TimescaleDatabaseResource = "benchmark-timescale";
    private const string ComparisonResource = "comparisons";
    private const string TimescaleImage = "timescale/timescaledb";
    private const string TimescaleDigest = "e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private static string SourceRevision => Environment.GetEnvironmentVariable("GITHUB_SHA")
        ?? throw new InvalidOperationException("The comparison run requires its GitHub source SHA.");

    [Test]
    public async Task AspireTimeseriesProfileEmitsOracleReportAndProtectsForeignSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-timeseries-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "reports");
        var evidence = EvidenceDirectory();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var adminKey = "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var arguments = new[]
        {
            $"--KeyLoad:DataRoot={Path.Combine(root, "cluster")}",
            "--KeyLoad:Ephemeral=true",
            "--Benchmarks:Enabled=true",
            "--Benchmarks:Profile=timeseries",
            $"--Benchmarks:DataRoot={root}",
            $"--Benchmarks:Output={output}",
            $"--Benchmarks:SourceRevision={SourceRevision}",
            $"--Parameters:admin-key={adminKey}"
        };
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await using var logs = new ComparisonTestLogCapture(app);
        string? cleanupImage = null;
        try
        {
            cleanupImage = VerifyTimescaleImage(app);
            await app.StartAsync(timeout.Token);
            await WaitForReadyTimescaleAsync(app, timeout.Token);
            var connectionString = await app.GetConnectionStringAsync(TimescaleDatabaseResource, timeout.Token)
                ?? throw new InvalidOperationException("Aspire did not resolve the Timescale connection string.");
            await WaitForComparisonAsync(app, timeout.Token);
            await TimeSeriesProfileReportAssertions.VerifyAsync(output, SourceRevision, timeout.Token);
            await TimeSeriesForeignSchemaCollision.VerifyAsync(connectionString, timeout.Token);
        }
        finally
        {
            await logs.StopAsync();
            try
            {
                await app.StopAsync(CancellationToken.None);
            }
            finally
            {
                await CaptureEvidenceAsync(output, evidence, logs);
                await DeleteTemporaryDataAsync(root, cleanupImage);
            }
        }
    }

    private static string VerifyTimescaleImage(DistributedApplication app)
    {
        var resource = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Single(item => item.Name == TimescaleServerResource);
        var imageAnnotation = resource.Annotations.OfType<ContainerImageAnnotation>().SingleOrDefault();
        if (imageAnnotation is null
            || !string.Equals(imageAnnotation.Image, TimescaleImage, StringComparison.Ordinal)
            || !string.Equals(imageAnnotation.SHA256, TimescaleDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Aspire Timescale resource is not pinned to the accepted multi-platform digest.");
        }

        if (!resource.TryGetContainerImageName(out var image)
            || image is null
            || !image.EndsWith("@sha256:" + TimescaleDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Aspire Timescale resource is not pinned to the accepted multi-platform digest.");
        }

        return image;
    }

    private static Task<ResourceEvent> WaitForReadyTimescaleAsync(DistributedApplication app, CancellationToken cancellationToken)
        => app.ResourceNotifications.WaitForResourceAsync(TimescaleServerResource,
            resource => resource.Snapshot.State?.Text == KnownResourceStates.Running, cancellationToken);

    private static async Task WaitForComparisonAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        await app.ResourceNotifications.WaitForResourceAsync(ComparisonResource,
            resource => resource.Snapshot.ExitCode is not null
                || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart, cancellationToken);
        await Assert.That(app.ResourceNotifications.TryGetCurrentState(ComparisonResource, out var state)).IsTrue();
        await Assert.That(state!.Snapshot.ExitCode).IsEqualTo(0);
    }

    private static async Task CaptureEvidenceAsync(string output, string evidence, ComparisonTestLogCapture logs)
    {
        Directory.CreateDirectory(evidence);
        await logs.WriteToAsync(Path.Combine(evidence, "timeseries-runner.log"));
        foreach (var file in Directory.Exists(output) ? Directory.EnumerateFiles(output) : [])
        {
            File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), true);
        }
    }

    private static string EvidenceDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx")))
        {
            repository = repository.Parent;
        }

        return Path.Combine(repository.FullName, "artifacts", "comparisons", "timeseries");
    }

    private static async Task DeleteTemporaryDataAsync(string root, string? cleanupImage)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsLinux())
        {
        }

        var cluster = Path.Combine(root, "cluster");
        if (!Directory.Exists(cluster) || cleanupImage is null)
        {
            throw new IOException("Cannot safely clean the comparison run's container-owned data.");
        }

        var start = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[]
        {
            "run", "--rm", "--pull", "never", "--network", "none", "--read-only", "--user", "0:0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--entrypoint", "/bin/sh",
            "--mount", $"type=bind,source={root},target=/data", cleanupImage, "-c", "rm -rf /data/cluster/*"
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Cannot start temporary data cleanup.");
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
            throw new IOException($"Temporary data cleanup failed: {await error}");
        }

        await error;
        Directory.Delete(root, recursive: true);
    }
}

internal static class TimeSeriesProfileReportAssertions
{
    private const string SchemaVersionProperty = "schemaVersion";
    private const string SourceRevisionProperty = "sourceRevision";
    private const string RunIdProperty = "runId";
    private const string WorkloadHashProperty = "workloadHash";
    private const string SampleCountProperty = "sampleCount";
    private const string TargetsProperty = "targets";
    private const string AttemptsProperty = "attempts";
    private const string CorrectnessChecksProperty = "correctnessChecks";
    private const string NameProperty = "name";
    private const string PersistenceGuaranteeProperty = "persistenceGuarantee";
    private const string AcknowledgementGuaranteeProperty = "acknowledgementGuarantee";
    private const string ImageProperty = "image";
    private const string PackageVersionProperty = "packageVersion";
    private const string StorageModelProperty = "storageModel";
    private const string SucceededProperty = "succeeded";
    private const string TargetProperty = "target";
    private const string OperationProperty = "operation";
    private const string CheckProperty = "check";
    private const string PassedProperty = "passed";
    private const string ExpectedCountProperty = "expectedCount";
    private const string ActualCountProperty = "actualCount";
    private const string ExpectedHashProperty = "expectedHash";
    private const string ActualHashProperty = "actualHash";
    private const string ErrorCodeProperty = "errorCode";
    private const string ReportName = "timeseries-results.json";
    private const string GeneralReportName = "results.json";
    private const string ExpectedWorkloadHash = "d67ed223ef6c701daaa06481aaeb83dd069133cb9fd7b7d017e380d83ef0d869";
    private const string TimescaleImageIdentity = "timescale/timescaledb:2.30.2-pg18@sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const string KeyLoadTarget = "KeyLoad TimeSeries";
    private const string TimescaleTarget = "TimescaleDB TimeSeries";
    private const string LibraryTarget = "ManagedCode.TimeSeries";

    internal static async Task VerifyAsync(string output, string sourceRevision, CancellationToken cancellationToken)
    {
        await Assert.That(sourceRevision.Length).IsEqualTo(40);
        await Assert.That(sourceRevision.All(Uri.IsHexDigit)).IsTrue();
        var reportPath = Path.Combine(output, ReportName);
        await Assert.That(File.Exists(reportPath)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, GeneralReportName))).IsFalse();
        await using var stream = File.OpenRead(reportPath);
        using var report = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = report.RootElement;
        await Assert.That(root.GetProperty(SchemaVersionProperty).GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty(SourceRevisionProperty).GetString()).IsEqualTo(sourceRevision);
        await Assert.That(root.GetProperty(RunIdProperty).GetString() is { Length: > 0 }).IsTrue();
        await Assert.That(root.GetProperty(WorkloadHashProperty).GetString()).IsEqualTo(ExpectedWorkloadHash);
        await Assert.That(root.GetProperty(SampleCountProperty).GetInt32()).IsEqualTo(48);
        await VerifyTargetsAsync(root.GetProperty(TargetsProperty), sourceRevision);
        await VerifyAttemptsAndChecksAsync(root.GetProperty(AttemptsProperty), root.GetProperty(CorrectnessChecksProperty));
    }

    private static async Task VerifyTargetsAsync(JsonElement targets, string sourceRevision)
    {
        await Assert.That(targets.GetArrayLength()).IsEqualTo(3);
        var keyLoad = FindTarget(targets, KeyLoadTarget);
        var timescale = FindTarget(targets, TimescaleTarget);
        var library = FindTarget(targets, LibraryTarget);
        await Assert.That(keyLoad.GetProperty(PersistenceGuaranteeProperty).GetString()).Contains("RF3 process-durable");
        await Assert.That(keyLoad.GetProperty(PersistenceGuaranteeProperty).GetString()).Contains("power-loss durability unqualified");
        await Assert.That(keyLoad.GetProperty(AcknowledgementGuaranteeProperty).GetString()).Contains("Quorum acknowledgement");
        await Assert.That(keyLoad.GetProperty(ImageProperty).GetString())
            .IsEqualTo("KeyLoad RF3 Dockerfile build at " + sourceRevision);
        await Assert.That(timescale.GetProperty(PersistenceGuaranteeProperty).GetString()).Contains("single-node");
        await Assert.That(timescale.GetProperty(AcknowledgementGuaranteeProperty).GetString()).Contains("one server");
        await Assert.That(timescale.GetProperty(ImageProperty).GetString()).Contains(TimescaleImageIdentity);
        await Assert.That(library.GetProperty(PackageVersionProperty).GetString()).IsEqualTo("10.0.0");
        await Assert.That(library.GetProperty(StorageModelProperty).GetString()).IsEqualTo("in-memory bucket aggregation");
        await Assert.That(library.GetProperty(PersistenceGuaranteeProperty).GetString()).Contains("no persistence, recovery, or replication");
        await Assert.That(library.GetProperty(AcknowledgementGuaranteeProperty).GetString()).Contains("in-process");
    }

    private static async Task VerifyAttemptsAndChecksAsync(JsonElement attempts, JsonElement checks)
    {
        await Assert.That(attempts.GetArrayLength()).IsGreaterThan(0);
        await Assert.That(attempts.EnumerateArray().All(attempt => attempt.GetProperty(SucceededProperty).GetBoolean())).IsTrue();
        foreach (var target in new[] { KeyLoadTarget, TimescaleTarget })
        {
            AssertAttempt(attempts, target, "idempotent-seed-retry");
            foreach (var range in new[]
            {
                (Name: "inclusive-full-range", Count: 48), (Name: "inclusive-boundary", Count: 4),
                (Name: "offset-normalized", Count: 4), (Name: "empty-range", Count: 0), (Name: "invalid-range", Count: 0)
            })
            {
                AssertAttempt(attempts, target, range.Name);
                await AssertCheckAsync(checks, target, range.Name, range.Count, range.Name != "invalid-range");
            }

            AssertAttempt(attempts, target, "cleanup");
        }

        AssertAttempt(attempts, TimescaleTarget, "bucket-sum-oracle");
        AssertAttempt(attempts, LibraryTarget, "bucket-sum-oracle");
        await AssertCheckAsync(checks, TimescaleTarget, "bucket-sum-oracle", 12, true);
        await AssertCheckAsync(checks, LibraryTarget, "bucket-sum-oracle", 12, true);
        var invalid = FindCheck(checks, TimescaleTarget, "invalid-range");
        await Assert.That(invalid.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo("BudgetExceeded");
        await Assert.That(FindCheck(checks, KeyLoadTarget, "invalid-range").GetProperty(ErrorCodeProperty).GetString())
            .IsEqualTo("BudgetExceeded");
    }

    private static JsonElement FindTarget(JsonElement targets, string name)
        => targets.EnumerateArray().Single(target => target.GetProperty(NameProperty).GetString() == name);

    private static void AssertAttempt(JsonElement attempts, string target, string operation)
        => _ = attempts.EnumerateArray().Single(attempt => attempt.GetProperty(TargetProperty).GetString() == target
            && attempt.GetProperty(OperationProperty).GetString() == operation && attempt.GetProperty(SucceededProperty).GetBoolean());

    private static async Task AssertCheckAsync(JsonElement checks, string target, string name, int expectedCount,
        bool verifyExactHash)
    {
        var check = FindCheck(checks, target, name);
        await Assert.That(check.GetProperty(PassedProperty).GetBoolean()).IsTrue();
        await Assert.That(check.GetProperty(ExpectedCountProperty).GetInt32()).IsEqualTo(expectedCount);
        await Assert.That(check.GetProperty(ActualCountProperty).GetInt32()).IsEqualTo(expectedCount);
        if (verifyExactHash)
        {
            await Assert.That(check.GetProperty(ActualHashProperty).GetString())
                .IsEqualTo(check.GetProperty(ExpectedHashProperty).GetString());
        }
    }

    private static JsonElement FindCheck(JsonElement checks, string target, string check)
        => checks.EnumerateArray().Single(item => item.GetProperty(TargetProperty).GetString() == target
            && item.GetProperty(CheckProperty).GetString() == check);
}
