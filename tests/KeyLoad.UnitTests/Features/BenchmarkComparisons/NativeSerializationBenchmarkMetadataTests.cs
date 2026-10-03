using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;
using Arguments = TUnit.Core.ArgumentsAttribute;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationBenchmarkMetadataTests
{
    [Test]
    [Arguments(typeof(NativeDocumentSerializationBenchmarks))]
    [Arguments(typeof(NativeCommandSerializationBenchmarks))]
    [Arguments(typeof(NativeStorageSerializationBenchmarks))]
    public async Task AcIsPerf001PublicFixturesExposeEightGenuineExternalCases(Type fixture)
    {
        await Assert.That(fixture.IsVisible && !fixture.IsSealed).IsTrue();
        await Assert.That(fixture.GetCustomAttributes(typeof(MemoryDiagnoserAttribute), false).Length).IsEqualTo(1);
        var cases = BenchmarkConverter.TypeToBenchmarks(fixture, ManualConfig.CreateMinimumViable()).BenchmarksCases;
        await Assert.That(cases.Length).IsEqualTo(8);
        var methods = cases.Select(item => item.Descriptor.WorkloadMethod.Name).Distinct(StringComparer.Ordinal).Order().ToArray();
        await Assert.That(methods).IsEquivalentTo(new[] { "JsonDecode", "JsonEncode", "NativeDecode", "NativeEncode" }, CollectionOrdering.Matching);
        var sizes = cases.Select(item => (int)item.Parameters.Items.Single().Value).Distinct().Order().ToArray();
        await Assert.That(sizes).IsEquivalentTo(new[] { 1024, 16384 }, CollectionOrdering.Matching);
        await Assert.That(cases.All(item => item.Parameters.Items.Single().Name == "PayloadBytes")).IsTrue();
        await Assert.That(cases.All(item => item.Job.Run.LaunchCount == 2 && item.Job.Run.WarmupCount == 3
            && item.Job.Run.IterationCount == 6)).IsTrue();
        await Assert.That(fixture.GetMethod("Setup")!.GetCustomAttributes(typeof(GlobalSetupAttribute), false).Length).IsEqualTo(1);
        await Assert.That(fixture.GetMethod("Cleanup")!.GetCustomAttributes(typeof(GlobalCleanupAttribute), false).Length).IsEqualTo(1);
    }
}
