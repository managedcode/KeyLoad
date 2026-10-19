using KeyLoad.Comparisons.Targets;
using KeyLoad.UnitTests;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004: declared benchmark contracts never imply an observed cluster.</summary>
internal sealed class BenchmarkTopologyTargetTests
{
    private const string ApiKey = "topology-test-key";
    private const string RunId = "topology-contract-test";
    private const string ClusterTopology = "3 Orleans silos, 3 data copies; independent ZoneTree storage on one host";
    private const string DatabaseReadCut = "authorized database read cut through Orleans";
    private const string BenchmarkAcknowledgement = "benchmark";
    private const string OpenQualification = "qualification pending";
    private const string ExpectedNodesParameter = "expectedNodes";

    [Test]
    public async Task AcIso004DefaultTargetRetainsItsRequiredClusterContract()
    {
        using var http = new HttpClient();
        await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Execution(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation());
        await Assert.That(target.Profile.Topology).IsEqualTo(ClusterTopology);
        await Assert.That(target.Profile.ReadContract).Contains(DatabaseReadCut);
        await Assert.That(target.Profile.Cluster).IsNull();
    }

    [Test]
    [Arguments(1, "1 Orleans silo", "1 data copy")]
    public async Task AcIso004BenchmarkTargetDeclaresRequiredCopiesWithoutInventingObservedAvailability(int nodes,
        string silos, string copies)
    {
        using var http = new HttpClient();
        await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Execution(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation(), expectedNodes: nodes);
        await Assert.That(target.Profile.Topology).Contains(silos);
        await Assert.That(target.Profile.Topology).Contains(copies);
        await Assert.That(target.Profile.ReadContract).Contains(DatabaseReadCut);
        await Assert.That(target.Profile.WriteAcknowledgement).Contains(BenchmarkAcknowledgement);
        await Assert.That(target.Profile.WriteAcknowledgement).Contains(OpenQualification);
        await Assert.That(target.Profile.Cluster).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    [Arguments(4)]
    public async Task AcIso003InvalidExpectedNodeCountFailsBeforeAnyDatabaseWork(int nodes)
    {
        using var http = new HttpClient();
        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Execution(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation(), expectedNodes: nodes);
        });
        await Assert.That(error!.ParamName).IsEqualTo(ExpectedNodesParameter);
    }
}
