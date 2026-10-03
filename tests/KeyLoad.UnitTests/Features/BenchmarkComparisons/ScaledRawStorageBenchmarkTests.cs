using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks genuine generated metadata and both real five-million-read methods.</summary>
[NotInParallel]
internal sealed class ScaledRawStorageBenchmarkTests
{
    private const string EngineEnvironmentVariable = "KEYLOAD_RAW_STORAGE_ENGINE";
    private const string RecordCountEnvironmentVariable = "KEYLOAD_SCALED_STORAGE_RECORD_COUNT";
    private const string ZoneTreeLabel = "zonetree";
    private const string RemovedEngineLabel = "tsavorite";
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int ExpectedBenchmarkCaseCount = 4;
    private const int MemoryDiagnoserCount = 1;
    private const int SimpleJobCount = 1;
    private const int HundredThousandRecords = 100_000;
    private const int OneMillionRecords = 1_000_000;
    private const int FiveMillionRecords = 5_000_000;
    private const int UnsupportedRecordCount = 200_000;
    private const int ReadsPerInvocation = 5_000_000;
    private const int InvocationCount = 1;
    private const int UnrollFactor = 1;
    private const int LaunchCount = 1;
    private const int WarmupCount = 8;
    private const int IterationCount = 10;
    private const string InvalidRecordCountMessage = "The scaled benchmark record-count environment value is unsupported.";
    private static readonly string[] ExpectedMethods =
    [
        nameof(ScaledStorageReadBenchmarks.RandomRead),
        nameof(ScaledStorageReadBenchmarks.SequentialRead)
    ];

    [Test]
    public async Task AcScale004ConverterBuildsOnlySelectedZoneTreeScaleAndPayloadCases()
    {
        var benchmarkType = typeof(ScaledStorageReadBenchmarks);
        await Assert.That(benchmarkType.IsVisible).IsTrue();
        await Assert.That(benchmarkType.IsSealed).IsFalse();
        await Assert.That(benchmarkType.GetCustomAttributes(typeof(MemoryDiagnoserAttribute), inherit: false).Length)
            .IsEqualTo(MemoryDiagnoserCount);
        await Assert.That(benchmarkType.GetCustomAttributes(typeof(SimpleJobAttribute), inherit: false).Length)
            .IsEqualTo(SimpleJobCount);

        var cases = BenchmarkConverter.TypeToBenchmarks(benchmarkType, ManualConfig.CreateMinimumViable()).BenchmarksCases;
        var methodNames = cases.Select(item => item.Descriptor.WorkloadMethod.Name)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(methodNames).IsEquivalentTo(ExpectedMethods, CollectionOrdering.Matching);
        await Assert.That(cases.Length).IsEqualTo(ExpectedBenchmarkCaseCount);
        await AssertParameterValuesAsync(cases);
        await AssertJobValuesAsync(cases);
        await AssertBenchmarkOperationsPerInvokeAsync(benchmarkType);
    }

    [Test]
    public async Task AcScale004BothMethodsConsumeExactFiveMillionZoneTreeReadIdentities()
    {
        await ScaledRawStorageTestLifetime.RunAsync(
            () => new ScaledStorageReadBenchmarks
            {
                Engine = ZoneTreeLabel,
                PayloadBytes = SmallPayloadBytes,
                RecordCount = HundredThousandRecords
            }, async benchmark =>
            {
                benchmark.Setup();
                var expectedChecksum = ExpectedChecksum(HundredThousandRecords);
                var beforeSequential = benchmark.Capture().NativeReadCalls;
                await Assert.That(benchmark.SequentialRead()).IsEqualTo(expectedChecksum);
                await Assert.That(benchmark.Capture().NativeReadCalls - beforeSequential).IsEqualTo((long)ReadsPerInvocation);
                var beforeRandom = benchmark.Capture().NativeReadCalls;
                await Assert.That(benchmark.RandomRead()).IsEqualTo(expectedChecksum);
                await Assert.That(benchmark.Capture().NativeReadCalls - beforeRandom).IsEqualTo((long)ReadsPerInvocation);
                benchmark.Cleanup();
            });
    }

    [Test]
    public async Task AcScale004SetupRejectsNonQualificationCountsBeforeFixtureAcquisition()
    {
        using var benchmark = new ScaledStorageReadBenchmarks
        {
            Engine = ZoneTreeLabel,
            PayloadBytes = SmallPayloadBytes,
            RecordCount = UnsupportedRecordCount
        };

        await Assert.That(benchmark.Setup).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AcScale004SetupRejectsUnknownEngineBeforeFixtureAcquisition()
    {
        using var benchmark = new ScaledStorageReadBenchmarks
        {
            Engine = "unsupported",
            PayloadBytes = SmallPayloadBytes,
            RecordCount = HundredThousandRecords
        };

        await Assert.That(benchmark.Setup).Throws<ArgumentException>();
    }

    [Test]
    public async Task AcScale004SetupRejectsRemovedTsavoriteLabelBeforeFixtureAcquisition()
    {
        using var benchmark = new ScaledStorageReadBenchmarks
        {
            Engine = RemovedEngineLabel,
            PayloadBytes = SmallPayloadBytes,
            RecordCount = HundredThousandRecords
        };

        await Assert.That(benchmark.Setup).Throws<ArgumentException>();
    }

    private static async Task AssertParameterValuesAsync(IReadOnlyList<BenchmarkCase> cases)
    {
        var engines = ParameterValues(cases, nameof(ScaledStorageReadBenchmarks.Engine));
        var records = ParameterValues(cases, nameof(ScaledStorageReadBenchmarks.RecordCount));
        var payloads = ParameterValues(cases, nameof(ScaledStorageReadBenchmarks.PayloadBytes));
        await Assert.That(engines.Length).IsEqualTo(SimpleJobCount);
        await Assert.That(engines[0])
            .IsEqualTo(Environment.GetEnvironmentVariable(EngineEnvironmentVariable) ?? ZoneTreeLabel);
        await Assert.That(records).IsEquivalentTo([ExpectedRecordCount().ToString(CultureInfo.InvariantCulture)],
            CollectionOrdering.Matching);
        var expectedPayloads = new[] { SmallPayloadBytes, LargePayloadBytes }
            .Select(size => size.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(payloads).IsEquivalentTo(expectedPayloads, CollectionOrdering.Matching);
    }

    private static async Task AssertJobValuesAsync(IReadOnlyList<BenchmarkCase> cases)
    {
        await Assert.That(cases.All(item => item.Job.Run.InvocationCount == InvocationCount
            && item.Job.Run.UnrollFactor == UnrollFactor
            && item.Job.Run.LaunchCount == LaunchCount
            && item.Job.Run.WarmupCount == WarmupCount
            && item.Job.Run.IterationCount == IterationCount
            && item.Job.Environment.Runtime?.RuntimeMoniker == RuntimeMoniker.Net10_0)).IsTrue();
    }

    private static async Task AssertBenchmarkOperationsPerInvokeAsync(Type benchmarkType)
    {
        foreach (var methodName in ExpectedMethods)
        {
            var method = benchmarkType.GetMethod(methodName);
            await Assert.That(method).IsNotNull();
            var attribute = method!.GetCustomAttributes(typeof(BenchmarkAttribute), inherit: false)
                .Cast<BenchmarkAttribute>().Single();
            await Assert.That(attribute.OperationsPerInvoke).IsEqualTo(ReadsPerInvocation);
        }
    }

    private static string[] ParameterValues(IReadOnlyList<BenchmarkCase> cases, string name)
        => cases.SelectMany(item => item.Parameters.Items)
            .Where(item => item.Name == name)
            .Select(item => Convert.ToString(item.Value, CultureInfo.InvariantCulture)!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static int ExpectedRecordCount()
    {
        var value = Environment.GetEnvironmentVariable(RecordCountEnvironmentVariable);
        if (value is null)
        {
            return HundredThousandRecords;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count is HundredThousandRecords or OneMillionRecords or FiveMillionRecords)
        {
            return count;
        }

        throw new InvalidOperationException(InvalidRecordCountMessage);
    }

    private static ulong ExpectedChecksum(int recordCount)
        => (ulong)(ReadsPerInvocation / recordCount) * (ulong)recordCount * (ulong)(recordCount - 1) / 2;
}
