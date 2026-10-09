using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004: declared benchmark contracts never imply an observed cluster.</summary>
internal sealed class BenchmarkTopologyTargetTests
{
    private const string ApiKey = "topology-test-key";
    private const string RunId = "topology-contract-test";
    private const string Rf3Topology = "3 voters, RF3, one physical shard; all processes on one host";
    private const string QuorumBarrier = "strong quorum barrier";
    private const string BenchmarkAcknowledgement = "benchmark";
    private const string QualifiedProcessKill = "process-kill qualified";
    private const string ExpectedNodesParameter = "expectedNodes";

    [Test]
    public async Task AcIso004DefaultTargetRetainsItsRequiredRf3Contract()
    {
        using var http = new HttpClient();
        await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation());
        await Assert.That(target.Profile.Topology).IsEqualTo(Rf3Topology);
        await Assert.That(target.Profile.ReadContract).Contains(QuorumBarrier);
        await Assert.That(target.Profile.Cluster).IsNull();
    }

    [Test]
    [Arguments(1, "RF1", "quorum 1 of 1", "no replica fault tolerance")]
    public async Task AcIso004BenchmarkTargetDeclaresActualQuorumAndNoFaultAvailability(int nodes,
        string replication, string quorum, string availability)
    {
        using var http = new HttpClient();
        await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation(), expectedNodes: nodes);
        await Assert.That(target.Profile.Topology).Contains(replication);
        await Assert.That(target.Profile.Topology).Contains(quorum);
        await Assert.That(target.Profile.Topology).Contains(availability);
        await Assert.That(target.Profile.ReadContract).Contains(QuorumBarrier);
        await Assert.That(target.Profile.WriteAcknowledgement).Contains(BenchmarkAcknowledgement);
        await Assert.That(target.Profile.WriteAcknowledgement).DoesNotContain(QualifiedProcessKill);
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
            await using var target = new KeyLoadTarget(http, ApiKey, RunId, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Diagnostics(), UnitBenchmarkOptions.KeyLoadAdmission(), UnitClientOptions.Execution(), UnitClientOptions.Translation(), expectedNodes: nodes);
        });
        await Assert.That(error!.ParamName).IsEqualTo(ExpectedNodesParameter);
    }
}
