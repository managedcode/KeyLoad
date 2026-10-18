using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyRouteTests
{
    private const string KeyLoad = "KeyLoad";
    private const string Redis = "Redis";
    private const string Neo4j = "Neo4j";
    private const string NodePrefix = "node";
    private const string SourceSetting = "Benchmarks__SourceRevision";
    private const string TargetSetting = "Benchmarks__Target";
    private const string CountSetting = "Benchmarks__NodeCount";
    private const string BenchmarkSetting = "KeyLoad__BenchmarkTopology";
    private const string EnabledSetting = "Benchmarks:Enabled";
    private const string EndpointsPrefix = IsolatedResourceTopologyFixture.NativePrefix + "Endpoints__";
    private const string ScaleProfileEnvironment = "Benchmarks__ScaleProfile";
    private const string EvidenceProfileEnvironment = "Benchmarks__EvidenceProfile";

    /// <summary>AC-ISO-001/003: actual AppHost entry selects exact native KRN nodes before legacy CLUSTER/TimeSeries.</summary>
    [Test]
    [Arguments(KeyLoad, 1)]
    [Arguments(KeyLoad, 3)]
    [Arguments(Redis, 1)]
    [Arguments(Redis, 3)]
    [Arguments(Neo4j, 1)]
    public async Task SelectedEntryBuildsOnlyTheRequestedNativeGroupAndSourceBoundRunner(string target, int count)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(target, count);
        var runner = resources.Single(resource => resource.Name == IsolatedResourceTopologyFixture.RunnerName);
        var nodes = resources.Where(resource => resource != runner).ToArray();
        await Assert.That(resources.Length).IsEqualTo(count + 1);
        await Assert.That(nodes.Select(node => node.Name)).IsEquivalentTo(ExpectedNodes(target, count));
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        var bindings = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(bindings[SourceSetting]).IsEqualTo(IsolatedResourceTopologyApplication.Source);
        await Assert.That(bindings[TargetSetting]).IsEqualTo(target);
        await Assert.That(bindings[EvidenceProfileEnvironment]).IsEqualTo(BenchmarkSessionContract.Profile);
        await Assert.That(bindings.ContainsKey(ScaleProfileEnvironment)).IsFalse();
        await Assert.That(int.Parse(bindings[CountSetting], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(count);
        await Assert.That(bindings.Keys.Count(key => key.StartsWith(EndpointsPrefix, StringComparison.Ordinal))).IsEqualTo(count);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
    }

    /// <summary>AC-ISO-003/006: actual unsupported route creates one reason worker and no database or credential.</summary>
    [Test]
    [Arguments(3)]
    public async Task UnsupportedCommunityReplicationEntryBuildsOnlyTheReasonWorker(int count)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(Neo4j, count);
        await Assert.That(resources.Length).IsEqualTo(1);
        var runner = resources[0];
        await Assert.That(runner.Name).IsEqualTo(IsolatedResourceTopologyFixture.RunnerName);
        await Assert.That(runner.Annotations.OfType<WaitAnnotation>().Any()).IsFalse();
        var bindings = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(bindings.Keys.Count(key => key.StartsWith(IsolatedResourceTopologyFixture.NativePrefix,
            StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(bindings[IsolatedResourceTopologyFixture.NativePrefix + "Image"])
            .IsEqualTo("docker.io/library/neo4j:2026.09.0@" + BenchmarkResources.Neo4jDigest);
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
    }

    /// <summary>AC-ISO-003: invalid selection fails before cluster files, reports or native resources exist.</summary>
    [Test]
    [Arguments(BenchmarkSessionSelection.TargetSetting, "Unknown")]
    [Arguments(BenchmarkSessionSelection.NodeCountSetting, "0")]
    [Arguments(BenchmarkSessionSelection.NodeCountSetting, "2")]
    [Arguments(BenchmarkSessionSelection.NodeCountSetting, "4")]
    [Arguments("Benchmarks:Scenario", "0")]
    [Arguments("Benchmarks:Profile", "legacy")]
    [Arguments("Benchmarks:ScaleProfile", "scaled-7m-c16")]
    [Arguments("Benchmarks:ScaleProfile", "Scaled-100k-c16")]
    public async Task InvalidSelectedEntryRejectsBeforeAllocatingACluster(string setting, string value)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () => { await model.BuildAsync(KeyLoad, 1, setting, value); }).Throws<Microsoft.Extensions.Options.OptionsValidationException>();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
    }

    /// <summary>AC-SCALE-014: explicit legacy workload knobs cannot partially override a closed scale profile.</summary>
    [Test]
    [Arguments("Benchmarks:Documents", "100000")]
    [Arguments("Benchmarks:Operations", "100000")]
    [Arguments("Benchmarks:Warmup", "256")]
    [Arguments("Benchmarks:Repetitions", "1")]
    [Arguments("Benchmarks:Concurrency", "16")]
    [Arguments("Benchmarks:PayloadBytes", "1024")]
    [Arguments("Benchmarks:Seed", "1729")]
    [Arguments("Benchmarks:Dimensions", "32")]
    [Arguments("Benchmarks:TopK", "10")]
    [Arguments("Benchmarks:TimeoutSeconds", "30")]
    [Arguments("Benchmarks:GraphVertices", "0")]
    [Arguments("Benchmarks:GraphFanOut", "0")]
    [Arguments("Benchmarks:GraphDepth", "0")]
    public async Task EveryRemovedWorkloadOverrideRejectsBeforeSessionAllocation(string setting, string value)
        => await RejectAsync(KeyLoad, setting, value);

    [Test]
    public async Task SessionRequiresExplicitBenchmarkModeBeforeNativeAllocation()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () => { await model.BuildAsync(KeyLoad, 1, EnabledSetting, "false"); })
            .Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
    }

    /// <summary>AC-ISO-004: normal production composition retains CLUSTER and cannot enable the benchmark opt-in.</summary>
    [Test]
    public async Task DefaultProductionEntryStillBuildsThreeVotersWithoutBenchmarkOptIn()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(null, 3);
        await Assert.That(resources.Select(resource => resource.Name)).IsEquivalentTo(ExpectedNodes(KeyLoad, 3));
        foreach (var node in resources)
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment.ContainsKey(BenchmarkSetting)).IsFalse();
        }
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsTrue();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
    }

    private static string[] ExpectedNodes(string target, int count)
        => target == Neo4j ? ["neo4j"] : Enumerable.Range(1, count)
            .Select(index => target == KeyLoad ? NodePrefix + index : index == 1 ? "primary" : "replica" + (index - 1)).ToArray();

    private static async Task RejectAsync(string target, string setting, string value)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () => { await model.BuildAsync(target, 1, setting, value); }).Throws<Microsoft.Extensions.Options.OptionsValidationException>();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
    }
}
