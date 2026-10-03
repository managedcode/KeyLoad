using System.Text.Json;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class NativeSerializationBenchmarkGeneratedConsumerTests
{
    private static readonly Type[] Fixtures =
    [
        typeof(NativeDocumentSerializationBenchmarks), typeof(NativeCommandSerializationBenchmarks), typeof(NativeStorageSerializationBenchmarks)
    ];

    [Test]
    public async Task AcIsPerf002CustomizedDryRunsExactlyTwentyFourRealExternalCases()
    {
        var parsed = ConfigParser.Parse(NativeSerializationBenchmarkProcess.DryArguments, NullLogger.Instance);
        await Assert.That(parsed.isSuccess).IsTrue();
        var cases = Fixtures.SelectMany(type => BenchmarkConverter.TypeToBenchmarks(type, parsed.config).BenchmarksCases).ToArray();
        await Assert.That(cases.Length).IsEqualTo(24);
        await Assert.That(cases.All(item => item.Job.Run.LaunchCount == 1 && item.Job.Run.WarmupCount == 0
            && item.Job.Run.IterationCount == 1 && item.Job.Run.RunStrategy == RunStrategy.ColdStart)).IsTrue();
        var directory = Path.Combine(EmbeddedBenchmarkProcess.RepositoryRoot(), "artifacts", "qualification",
            "native-serialization-dry-" + Guid.NewGuid().ToString("N"));
        var exitCode = await NativeSerializationBenchmarkProcess.RunAsync(directory, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(exitCode).IsEqualTo(0);
        var evidence = await ReadEvidence(directory);
        var expected = cases.Select(item => item.Descriptor.Type.Name + "/" + item.Descriptor.WorkloadMethod.Name
            + "/PayloadBytes=" + item.Parameters.Items.Single().Value).ToHashSet(StringComparer.Ordinal);
        await Assert.That(evidence.SetEquals(expected)).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(directory, "corpus"), "*.json").Length).IsEqualTo(6);
    }

    private static async Task<HashSet<string>> ReadEvidence(string directory)
    {
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*-report-full.json", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(path);
            using var report = await JsonDocument.ParseAsync(stream);
            foreach (var benchmark in report.RootElement.GetProperty(NativeSerializationReportFields.Benchmarks).EnumerateArray())
            {
                await Assert.That(benchmark.GetProperty(NativeSerializationReportFields.Statistics).ValueKind).IsEqualTo(JsonValueKind.Object);
                await Assert.That(PositiveResult(benchmark.GetProperty(NativeSerializationReportFields.Measurements))).IsTrue();
                var key = benchmark.GetProperty(NativeSerializationReportFields.Type).GetString() + "/" + benchmark.GetProperty(NativeSerializationReportFields.Method).GetString()
                    + "/" + benchmark.GetProperty(NativeSerializationReportFields.Parameters).GetString();
                await Assert.That(evidence.Add(key)).IsTrue();
            }
        }
        return evidence;
    }

    private static bool PositiveResult(JsonElement measurements)
        => measurements.EnumerateArray().Any(item => item.GetProperty(NativeSerializationReportFields.IterationMode).GetString() == "Workload"
            && item.GetProperty(NativeSerializationReportFields.IterationStage).GetString() == "Result"
            && item.GetProperty(NativeSerializationReportFields.Operations).GetInt64() > 0 && item.GetProperty(NativeSerializationReportFields.Nanoseconds).GetDouble() > 0);
}
