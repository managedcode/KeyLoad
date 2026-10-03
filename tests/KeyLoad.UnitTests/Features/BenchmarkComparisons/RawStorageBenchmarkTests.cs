using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;
using Arguments = TUnit.Core.ArgumentsAttribute;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies generated metadata and invokes every timed operation on real fixtures.</summary>
[NotInParallel]
internal sealed class RawStorageBenchmarkTests
{
    private const string EngineEnvironmentVariable = "KEYLOAD_RAW_STORAGE_ENGINE";
    private const string ZoneTreeLabel = "zonetree";
    private const string TsavoriteLabel = "tsavorite";
    private const int BenchmarkRecordCount = 4096;
    private const int TestRecordCount = 3;
    private const int WriteBudget = 32;
    private const int ExpectedCaseCount = 8;
    private const int DefaultInvocationCount = 1024;
    private const int DefaultUnrollFactor = 1;
    private const string WrongCaseEngineLabel = "ZoneTree";
    private const string UnsupportedEngineMessage = "The test process has an unsupported raw-storage engine label.";
    private static readonly int[] PayloadSizes = [32, 1024];
    private static readonly string[] ExpectedMethodNames =
    [
        nameof(RawStorageBenchmarks.CreateDelete),
        nameof(RawStorageBenchmarks.MissingRead),
        nameof(RawStorageBenchmarks.Overwrite),
        nameof(RawStorageBenchmarks.PointRead)
    ];

    [Test]
    public async Task AcGe004ConverterDiscoversPublicFixtureMethodsAndFrozenParameters()
    {
        var fixtureType = typeof(RawStorageBenchmarks);
        await Assert.That(fixtureType.IsVisible).IsTrue();
        await Assert.That(fixtureType.IsSealed).IsFalse();
        await Assert.That(fixtureType.GetCustomAttributes(typeof(MemoryDiagnoserAttribute), inherit: false).Length)
            .IsEqualTo(1);
        await Assert.That(fixtureType.GetCustomAttributes(typeof(SimpleJobAttribute), inherit: false).Length)
            .IsEqualTo(1);

        var config = ManualConfig.CreateMinimumViable();
        var benchmarkCases = BenchmarkConverter.TypeToBenchmarks(fixtureType, config).BenchmarksCases;
        await Assert.That(benchmarkCases.Length).IsEqualTo(ExpectedCaseCount);
        var methodNames = benchmarkCases.Select(benchmark => benchmark.Descriptor.WorkloadMethod.Name)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(methodNames).IsEquivalentTo(ExpectedMethodNames, CollectionOrdering.Matching);
        await AssertParameterValuesAsync(benchmarkCases);
        var setup = fixtureType.GetMethod(nameof(RawStorageBenchmarks.Setup));
        var cleanup = fixtureType.GetMethod(nameof(RawStorageBenchmarks.Cleanup));
        await Assert.That(setup).IsNotNull();
        await Assert.That(cleanup).IsNotNull();
        await Assert.That(setup!.GetCustomAttributes(typeof(GlobalSetupAttribute), inherit: false).Length).IsEqualTo(1);
        await Assert.That(cleanup!.GetCustomAttributes(typeof(GlobalCleanupAttribute), inherit: false).Length).IsEqualTo(1);
        var createDelete = fixtureType.GetMethod(nameof(RawStorageBenchmarks.CreateDelete));
        await Assert.That(createDelete).IsNotNull();
        var benchmarkAttribute = createDelete!.GetCustomAttributes(typeof(BenchmarkAttribute), inherit: false)
            .Cast<BenchmarkAttribute>().Single();
        await Assert.That(benchmarkAttribute.OperationsPerInvoke).IsEqualTo(2);
        await Assert.That(string.IsNullOrWhiteSpace(benchmarkAttribute.Description)).IsFalse();
        await Assert.That(typeof(IDisposable).IsAssignableFrom(fixtureType)).IsTrue();
    }

    [Test]
    [Arguments(RawStorageEngineKind.ZoneTree, 32)]
    [Arguments(RawStorageEngineKind.ZoneTree, 1024)]
    [Arguments(RawStorageEngineKind.Tsavorite, 32)]
    [Arguments(RawStorageEngineKind.Tsavorite, 1024)]
    public async Task AcGe004AllMethodsUseRealFixtureAndReturnTheirActualOperationResult(
        RawStorageEngineKind engine, int payloadBytes)
    {
        using var oracle = new RawStorageFixture(engine, TestRecordCount, payloadBytes, WriteBudget);
        var original = oracle.Corpus.Value(0, alternate: false).ToArray();
        var alternate = oracle.Corpus.Value(0, alternate: true).ToArray();
        var benchmark = new RawStorageBenchmarks
        {
            Engine = EngineLabel(engine),
            PayloadBytes = payloadBytes,
            RecordCount = TestRecordCount
        };

        try
        {
            benchmark.Setup();
            await Assert.That(benchmark.PointRead()).IsEqualTo(original[0]);
            await Assert.That(benchmark.MissingRead()).IsFalse();
            benchmark.Overwrite();
            await Assert.That(benchmark.PointRead()).IsEqualTo(alternate[0]);
            benchmark.Overwrite();
            await Assert.That(benchmark.PointRead()).IsEqualTo(original[0]);
            await Assert.That(benchmark.CreateDelete()).IsTrue();
        }
        finally
        {
            benchmark.Cleanup();
            benchmark.Dispose();
            benchmark.Cleanup();
        }
    }

    [Test]
    [Arguments(RawStorageEngineKind.ZoneTree, 32)]
    [Arguments(RawStorageEngineKind.ZoneTree, 1024)]
    [Arguments(RawStorageEngineKind.Tsavorite, 32)]
    [Arguments(RawStorageEngineKind.Tsavorite, 1024)]
    public async Task AcGe004AllTimedMethodsRejectCallsAfterRealCleanup(RawStorageEngineKind engine, int payloadBytes)
    {
        using var benchmark = new RawStorageBenchmarks
        {
            Engine = EngineLabel(engine),
            PayloadBytes = payloadBytes,
            RecordCount = TestRecordCount
        };
        benchmark.Setup();
        benchmark.Cleanup();
        benchmark.Dispose();
        benchmark.Cleanup();

        await Assert.That(benchmark.PointRead).Throws<ObjectDisposedException>();
        await Assert.That(benchmark.MissingRead).Throws<ObjectDisposedException>();
        await Assert.That(benchmark.Overwrite).Throws<ObjectDisposedException>();
        await Assert.That(benchmark.CreateDelete).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task AcGe004UnsupportedEngineLabelFailsBeforeBenchmarkSetup()
    {
        using var benchmark = new RawStorageBenchmarks
        {
            Engine = WrongCaseEngineLabel,
            PayloadBytes = PayloadSizes[0],
            RecordCount = TestRecordCount
        };

        await Assert.That(benchmark.Setup).Throws<ArgumentException>();
    }

    private static async Task AssertParameterValuesAsync(IReadOnlyList<BenchmarkCase> benchmarkCases)
    {
        var engineLabels = ParameterValues(benchmarkCases, nameof(RawStorageBenchmarks.Engine));
        var payloadSizes = ParameterValues(benchmarkCases, nameof(RawStorageBenchmarks.PayloadBytes));
        var recordCounts = ParameterValues(benchmarkCases, nameof(RawStorageBenchmarks.RecordCount));
        await Assert.That(benchmarkCases.All(benchmark => benchmark.Job.Run.InvocationCount == DefaultInvocationCount
            && benchmark.Job.Run.UnrollFactor == DefaultUnrollFactor)).IsTrue();
        await Assert.That(engineLabels).IsEquivalentTo(new[] { ExpectedEngineLabel() }, CollectionOrdering.Matching);
        await Assert.That(payloadSizes).IsEquivalentTo(PayloadSizes.Select(size => size.ToString(
            System.Globalization.CultureInfo.InvariantCulture)).ToArray(), CollectionOrdering.Matching);
        await Assert.That(recordCounts).IsEquivalentTo(new[] { BenchmarkRecordCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture) }, CollectionOrdering.Matching);
    }

    private static string[] ParameterValues(IReadOnlyList<BenchmarkCase> benchmarkCases, string parameterName)
        => benchmarkCases.SelectMany(benchmark => benchmark.Parameters.Items)
            .Where(parameter => parameter.Name == parameterName)
            .Select(parameter => Convert.ToString(parameter.Value, System.Globalization.CultureInfo.InvariantCulture)!)
            .Distinct(StringComparer.Ordinal).ToArray();

    private static string ExpectedEngineLabel()
    {
        var configured = Environment.GetEnvironmentVariable(EngineEnvironmentVariable);
        var expected = configured ?? ZoneTreeLabel;
        if (expected is not (ZoneTreeLabel or TsavoriteLabel))
        {
            throw new InvalidOperationException(UnsupportedEngineMessage);
        }

        return expected;
    }

    private static string EngineLabel(RawStorageEngineKind engine)
        => engine switch
        {
            RawStorageEngineKind.ZoneTree => ZoneTreeLabel,
            RawStorageEngineKind.Tsavorite => TsavoriteLabel,
            _ => throw new ArgumentOutOfRangeException(nameof(engine))
        };
}
