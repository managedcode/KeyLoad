using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[NotInParallel]
internal sealed class SampleChunkBenchmarkConsumerTests
{
    private static readonly string[] DryArguments =
    [
        "--job", "Dry", "--launchCount", "1", "--warmupCount", "0", "--iterationCount", "1", "--iterationTime", "1",
        "--outliers", "DontRemove",
        "--exporters", "fulljson", "--filter", "*SampleChunkSerializationBenchmarks*", "--keepFiles", "--stopOnFirstError"
    ];

    [Test]
    public async Task AcChunk006ActualExternalConsumerRunsAllThirtySixMatchedCases()
    {
        var parsed = ConfigParser.Parse(DryArguments, NullLogger.Instance);
        await Assert.That(parsed.isSuccess).IsTrue();
        var cases = BenchmarkConverter.TypeToBenchmarks(typeof(SampleChunkSerializationBenchmarks), parsed.config).BenchmarksCases;
        await Assert.That(cases.Length).IsEqualTo(36);
        await Assert.That(cases.All(item => item.Job.Run.LaunchCount == 1 && item.Job.Run.WarmupCount == 0
            && item.Job.Run.IterationCount == 1 && item.Job.Run.RunStrategy == RunStrategy.ColdStart)).IsTrue();
        var directory = Path.Combine(EmbeddedBenchmarkProcess.RepositoryRoot(), "artifacts", "qualification",
            "sample-chunk-dry-" + Guid.NewGuid().ToString("N"));
        var captureDirectory = directory + "-host";
        Directory.CreateDirectory(captureDirectory);
        var exit = await NativeSerializationBenchmarkProcess.RunProcessAsync(StartInfo(directory), captureDirectory,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(exit).IsEqualTo(0);
        var expected = cases.Select(item => item.Descriptor.WorkloadMethod.Name + "/"
                + string.Join("&", item.Parameters.Items.Select(parameter => parameter.Name + "="
                    + Convert.ToString(parameter.Value, CultureInfo.InvariantCulture))))
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That((await Evidence(directory)).SetEquals(expected)).IsTrue();
        await SampleChunkBenchmarkRejectionOracle.VerifyAsync(directory,
            TestContext.Current!.Execution.CancellationToken);
    }

    private static ProcessStartInfo StartInfo(string directory)
    {
        var root = EmbeddedBenchmarkProcess.RepositoryRoot();
        var assembly = Path.Combine(root, "benchmarks", "KeyLoad.Benchmarks", "bin", "Release", "net10.0", "KeyLoad.Benchmarks.dll");
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException("The Release sample chunk benchmark executable is not built.", assembly);
        }
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "Features", "BenchmarkComparisons", "sample-chunk-development.mjs"));
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add("--dry");
        return start;
    }

    private static async Task<HashSet<string>> Evidence(string directory)
    {
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*-report-full.json", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(path);
            using var report = await JsonDocument.ParseAsync(stream);
            foreach (var item in report.RootElement.GetProperty(NativeSerializationReportFields.Benchmarks).EnumerateArray())
            {
                await Assert.That(item.GetProperty(NativeSerializationReportFields.Type).GetString())
                    .IsEqualTo(nameof(SampleChunkSerializationBenchmarks));
                await Assert.That(item.GetProperty(NativeSerializationReportFields.Statistics).ValueKind).IsEqualTo(JsonValueKind.Object);
                await Assert.That(PositiveMeasurement(item.GetProperty(NativeSerializationReportFields.Measurements))).IsTrue();
                var key = item.GetProperty(NativeSerializationReportFields.Method).GetString() + "/"
                    + item.GetProperty(NativeSerializationReportFields.Parameters).GetString();
                await Assert.That(evidence.Add(key)).IsTrue();
            }
        }
        return evidence;
    }

    private static bool PositiveMeasurement(JsonElement measurements)
        => measurements.EnumerateArray().Any(item => item.GetProperty(NativeSerializationReportFields.IterationMode).GetString() == "Workload"
            && item.GetProperty(NativeSerializationReportFields.IterationStage).GetString() == "Result"
            && item.GetProperty(NativeSerializationReportFields.Operations).GetInt64() > 0
            && item.GetProperty(NativeSerializationReportFields.Nanoseconds).GetDouble() > 0);
}
