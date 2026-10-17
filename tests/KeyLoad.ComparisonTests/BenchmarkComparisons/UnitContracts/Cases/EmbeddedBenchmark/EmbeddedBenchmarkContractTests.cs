using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Proves the embedded benchmark fixture contract and its generated consumer.</summary>
[NotInParallel]
internal sealed class EmbeddedBenchmarkContractTests
{
    private const string TemporaryDirectoryPrefix = "keyload-benchmark-";
    private const string BenchmarkDocumentId = "1";
    private const string BenchmarkDocumentJson = "{\"text\":\"clustered document database\",\"number\":42}";
    private const string TenantKeyPart = "tenant";
    private const string DatabaseKeyPart = "database";
    private const string PartitionKeyPart = "partition";
    private const decimal CompositeKeyNumber = 42m;
    private const string DocumentKeyPart = "document";
    private const double ExpectedCosineSimilarity = 10d / 17d;
    private const double CosineTolerance = 1e-12;
    private static readonly string[] ExpectedMethodNames =
    [
        nameof(EmbeddedBenchmarks.PointRead),
        nameof(EmbeddedBenchmarks.CompositeKey),
        nameof(EmbeddedBenchmarks.ExactCosine)
    ];

    [Test]
    public async Task AcEm001ConverterDiscoversVisibleUnsealedFixtureAndPublicMethods()
    {
        var fixtureType = typeof(EmbeddedBenchmarks);
        await Assert.That(fixtureType.IsVisible).IsTrue();
        await Assert.That(fixtureType.IsSealed).IsFalse();
        await Assert.That(fixtureType.GetCustomAttributes(typeof(MemoryDiagnoserAttribute), inherit: false).Length).IsEqualTo(1);

        var config = ManualConfig.CreateMinimumViable().AddJob(Job.Dry);
        var runInfo = BenchmarkConverter.TypeToBenchmarks(fixtureType, config);
        var actualMethodNames = runInfo.BenchmarksCases
            .Select(benchmark => benchmark.Descriptor.WorkloadMethod.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedMethodNames = ExpectedMethodNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        await Assert.That(actualMethodNames).IsEquivalentTo(expectedMethodNames, CollectionOrdering.Matching);
        await Assert.That(fixtureType.GetMethod(nameof(EmbeddedBenchmarks.Setup))).IsNotNull();
        await Assert.That(fixtureType.GetMethod(nameof(EmbeddedBenchmarks.Cleanup))).IsNotNull();
        await Assert.That(fixtureType.GetMethod(nameof(EmbeddedBenchmarks.Setup))!
            .GetCustomAttributes(typeof(GlobalSetupAttribute), inherit: false).Length).IsEqualTo(1);
        await Assert.That(fixtureType.GetMethod(nameof(EmbeddedBenchmarks.Cleanup))!
            .GetCustomAttributes(typeof(GlobalCleanupAttribute), inherit: false).Length).IsEqualTo(1);
    }

    [Test]
    public async Task AcEm002RealFixtureSetupRunsOperationsAndCleansStoreIdempotently()
    {
        var temporaryRoot = Path.GetTempPath();
        var directoriesBefore = BenchmarkDirectories(temporaryRoot);
        var fixture = new EmbeddedBenchmarks();
        try
        {
            fixture.Setup();

            var document = fixture.PointRead();
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Reference.Id).IsEqualTo(BenchmarkDocumentId);
            await Assert.That(document!.Json).IsEqualTo(BenchmarkDocumentJson);
            var expectedCompositeKey = KeyCodec.Encode(TenantKeyPart, DatabaseKeyPart, PartitionKeyPart,
                CompositeKeyNumber, DocumentKeyPart);
            await Assert.That(fixture.CompositeKey()).IsEquivalentTo(expectedCompositeKey, CollectionOrdering.Matching);
            await Assert.That(Math.Abs(fixture.ExactCosine() - ExpectedCosineSimilarity) < CosineTolerance).IsTrue();
        }
        finally
        {
            fixture.Cleanup();
            fixture.Dispose();
            fixture.Cleanup();
        }

        await Assert.That(fixture.PointRead).Throws<InvalidOperationException>();
        var directoriesAfter = BenchmarkDirectories(temporaryRoot);
        await Assert.That(directoriesAfter).IsEquivalentTo(directoriesBefore, CollectionOrdering.Matching);

        var uninitializedFixture = new EmbeddedBenchmarks();
        uninitializedFixture.Cleanup();
        uninitializedFixture.Dispose();
        uninitializedFixture.Cleanup();
    }

    private static string[] BenchmarkDirectories(string root)
        => Directory.EnumerateDirectories(root, TemporaryDirectoryPrefix + "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
}
