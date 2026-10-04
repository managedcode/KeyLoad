using System.Text.Json;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies reports and ownership from the real generated benchmark consumer process.</summary>
[NotInParallel]
internal sealed class EmbeddedBenchmarkGeneratedConsumerTests
{
    private const string QualificationDirectoryName = "qualification";
    private const string ArtifactsDirectoryName = "artifacts";
    private const string ProcessOutputDirectoryPrefix = "embedded-benchmark-dry-";
    private const string CancellationOutputDirectoryPrefix = "embedded-benchmark-cancelled-";
    private const string TimeoutOutputDirectoryPrefix = "embedded-benchmark-timeout-";
    private const string StandardOutputFileName = "host.stdout.txt";
    private const string StandardErrorFileName = "host.stderr.txt";
    private const string FullJsonArtifactPattern = "*-report-full.json";
    private const string BenchmarksProperty = "Benchmarks";
    private const string MethodProperty = "Method";
    private const string StatisticsProperty = "Statistics";
    private const string MeasurementsProperty = "Measurements";
    private const string IterationModeProperty = "IterationMode";
    private const string IterationStageProperty = "IterationStage";
    private const string OperationsProperty = "Operations";
    private const string NanosecondsProperty = "Nanoseconds";
    private const string WorkloadIterationMode = "Workload";
    private const string ResultIterationStage = "Result";

    private static readonly string[] ExpectedMethodNames =
    [
        nameof(EmbeddedBenchmarks.PointRead),
        nameof(EmbeddedBenchmarks.CompositeKey),
        nameof(EmbeddedBenchmarks.ExactCosine)
    ];

    [Test]
    public async Task AcEm003GeneratedDryConsumerExportsThreeSuccessfulMeasurements()
    {
        var artifactDirectory = CreateArtifactDirectory(ProcessOutputDirectoryPrefix);
        var result = await EmbeddedBenchmarkProcess.RunAsync(artifactDirectory,
            TestContext.Current!.Execution.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, StandardOutputFileName), result.StandardOutput);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, StandardErrorFileName), result.StandardError);

        await Assert.That(result.ExitCode).IsEqualTo(EmbeddedBenchmarkProcess.SuccessExitCode);
        var evidence = await ReadBenchmarkEvidenceAsync(artifactDirectory);
        var actualMethodNames = evidence.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expectedMethodNames = ExpectedMethodNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        await Assert.That(actualMethodNames).IsEquivalentTo(expectedMethodNames, CollectionOrdering.Matching);
        foreach (var methodEvidence in evidence.Values)
        {
            await Assert.That(methodEvidence.HasStatistics).IsTrue();
            await Assert.That(methodEvidence.HasPositiveResultMeasurement).IsTrue();
        }
    }

    [Test]
    public async Task AcEm003CancellationStopsAndObservesTheRealConsumerProcess()
    {
        var artifactDirectory = CreateArtifactDirectory(CancellationOutputDirectoryPrefix);
        using var cancellation = new CancellationTokenSource();
        var execution = EmbeddedBenchmarkProcess.RunAsync(artifactDirectory, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.That(() => ObserveAsync(execution)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task AcEm003TimeoutStopsAndObservesTheRealConsumerProcess()
    {
        var artifactDirectory = CreateArtifactDirectory(TimeoutOutputDirectoryPrefix);
        await Assert.That(() => ObserveAsync(EmbeddedBenchmarkProcess.RunAsync(artifactDirectory, TimeSpan.Zero,
            CancellationToken.None))).Throws<TimeoutException>();
    }

    private static async Task ObserveAsync(Task<EmbeddedBenchmarkProcessResult> execution)
        => _ = await execution;

    private static string CreateArtifactDirectory(string prefix)
    {
        var repositoryRoot = EmbeddedBenchmarkProcess.RepositoryRoot();
        var artifactDirectory = Path.Combine(repositoryRoot, ArtifactsDirectoryName, QualificationDirectoryName,
            prefix + Guid.NewGuid().ToString(EmbeddedBenchmarkProcess.GuidFormat));
        Directory.CreateDirectory(artifactDirectory);
        return artifactDirectory;
    }

    private static async Task<Dictionary<string, BenchmarkEvidence>> ReadBenchmarkEvidenceAsync(string artifactDirectory)
    {
        var evidence = new Dictionary<string, BenchmarkEvidence>(StringComparer.Ordinal);
        var reportPaths = Directory.EnumerateFiles(artifactDirectory, FullJsonArtifactPattern, SearchOption.AllDirectories);
        foreach (var reportPath in reportPaths)
        {
            await using var reportFile = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4_096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var report = await JsonDocument.ParseAsync(reportFile);
            if (!report.RootElement.TryGetProperty(BenchmarksProperty, out var benchmarks))
            {
                continue;
            }

            foreach (var benchmark in benchmarks.EnumerateArray())
            {
                var methodName = benchmark.GetProperty(MethodProperty).GetString()!;
                var hasStatistics = benchmark.GetProperty(StatisticsProperty).ValueKind == JsonValueKind.Object;
                var hasMeasurement = HasPositiveResultMeasurement(benchmark.GetProperty(MeasurementsProperty));
                await Assert.That(evidence.TryAdd(methodName, new(hasStatistics, hasMeasurement))).IsTrue();
            }
        }

        return evidence;
    }

    private static bool HasPositiveResultMeasurement(JsonElement measurements)
        => measurements.EnumerateArray().Any(measurement =>
            measurement.GetProperty(IterationModeProperty).GetString() == WorkloadIterationMode
            && measurement.GetProperty(IterationStageProperty).GetString() == ResultIterationStage
            && measurement.GetProperty(OperationsProperty).GetInt64() > 0
            && measurement.GetProperty(NanosecondsProperty).GetDouble() > 0);

    private sealed record BenchmarkEvidence(bool HasStatistics, bool HasPositiveResultMeasurement);
}
