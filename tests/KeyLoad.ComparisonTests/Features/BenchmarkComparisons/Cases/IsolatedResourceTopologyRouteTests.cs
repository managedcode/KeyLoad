using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyRouteTests
{
    private const string ScenarioEnvironment = "Benchmarks__Scenario";
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

    /// <summary>AC-SCALE-014: each literal scale profile flows through the existing isolated native resource model.</summary>
    [Test]
    [Arguments("scaled-100k-c16")]
    [Arguments("scaled-1m-c16")]
    public async Task ScaledSelectionForwardsExactProfileAndPreservesNativeCell(string profile)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(KeyLoad, 1, scaleProfile: profile);
        var runner = resources.Single(resource => resource.Name == IsolatedResourceTopologyFixture.RunnerName);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(resources.Length).IsEqualTo(2);
        await Assert.That(resources.Select(resource => resource.Name)).IsEquivalentTo(new[] { "node1", "comparisons" });
        await Assert.That(environment[ScaleProfileEnvironment]).IsEqualTo(profile);
        await Assert.That(environment[EvidenceProfileEnvironment]).IsEqualTo(profile);
        await Assert.That(environment[SourceSetting]).IsEqualTo(IsolatedResourceTopologyApplication.Source);
        await Assert.That(environment[TargetSetting]).IsEqualTo(KeyLoad);
        await Assert.That(int.Parse(environment[CountSetting], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(1);
        await Assert.That(environment[ScenarioEnvironment]).IsEqualTo(nameof(Scenario.PointRead));
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, [resources.Single(resource => resource.Name == "node1")]);
    }

    /// <summary>AC-SCALE-014: a genuinely unsupported native topology retains its explicit disposition.</summary>
    [Test]
    public async Task ScaledSelectionPreservesUnsupportedNativeTopologyDisposition()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(Neo4j, 3, scaleProfile: "scaled-100k-c16");
        var runner = resources.Single();
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(environment[TargetSetting]).IsEqualTo(Neo4j);
        await Assert.That(int.Parse(environment[CountSetting], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(3);
        await Assert.That(environment[ScaleProfileEnvironment]).IsEqualTo("scaled-100k-c16");
        await Assert.That(resources.Length).IsEqualTo(1);
        await Assert.That(runner.Annotations.OfType<WaitAnnotation>().Any()).IsFalse();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
    }

    /// <summary>AC-ISO-001/003: actual AppHost entry selects exact native KRN nodes before legacy RF3/TimeSeries.</summary>
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
        await Assert.That(bindings[EvidenceProfileEnvironment]).IsEqualTo(IsolatedComparisonContract.Current.Profile);
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
    [Arguments(ComparisonWorkerSelection.TargetSetting, "Unknown")]
    [Arguments(ComparisonWorkerSelection.NodeCountSetting, "0")]
    [Arguments(ComparisonWorkerSelection.NodeCountSetting, "2")]
    [Arguments(ComparisonWorkerSelection.NodeCountSetting, "4")]
    [Arguments(ComparisonWorkerSelection.ScenarioSetting, "0")]
    [Arguments(ComparisonWorkerSelection.ProfileSetting, "legacy")]
    [Arguments(ComparisonWorkerSelection.ScaleProfileSetting, "scaled-7m-c16")]
    [Arguments(ComparisonWorkerSelection.ScaleProfileSetting, "Scaled-100k-c16")]
    [Arguments(ComparisonWorkerSelection.ScaleProfileSetting, "")]
    [Arguments(EnabledSetting, "false")]
    public async Task InvalidSelectedEntryRejectsBeforeAllocatingACluster(string setting, string value)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () => { await model.BuildAsync(KeyLoad, 1, setting, value); }).Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
    }

    /// <summary>AC-SCALE-014: mixed profile modes and non-isolated selection fail before creating resources.</summary>
    [Test]
    public async Task MixedScaleSelectorsRejectBeforeCreatingResources()
    {
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", evidenceProfile: IsolatedComparisonContract.Current.Profile);
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", appHostProfile: "timeseries");
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", appHostProfile: "other");
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", invalidSetting: EnabledSetting, invalidValue: "false");
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", invalidSetting: "Benchmarks:Operations", invalidValue: "100001");
        await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16",
            invalidSetting: ComparisonWorkerSelection.ScenarioSetting, invalidValue: "GraphTraverse");
        await RejectAsync(null, scaleProfile: "scaled-100k-c16");
        await RejectAsync(null, scaleProfile: "scaled-100k-c16", invalidSetting: "KeyLoadTests:Suite", invalidValue: "comparison");
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
    public async Task EveryLegacyWorkloadOverrideRejectsScaledSelection(string setting, string value)
        => await RejectAsync(KeyLoad, scaleProfile: "scaled-100k-c16", invalidSetting: setting, invalidValue: value);

    /// <summary>AC-ISO-004: normal production composition retains RF3 and cannot enable the benchmark opt-in.</summary>
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

    private static async Task RejectAsync(string? target, string scaleProfile, string? evidenceProfile = null,
        string? appHostProfile = null, string? invalidSetting = null, string? invalidValue = null)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () =>
        {
            await model.BuildAsync(target, 1, invalidSetting, invalidValue, scaleProfile, evidenceProfile, appHostProfile);
        }).Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, "native"))).IsFalse();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
    }
}
