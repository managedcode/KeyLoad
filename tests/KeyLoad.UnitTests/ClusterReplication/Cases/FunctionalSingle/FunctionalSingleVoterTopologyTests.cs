using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class FunctionalSingleVoterTopologyTests
{
    private const int SingleVoter = 1;
    private const int Rf3Majority = 2;

    [Test]
    public async Task ExplicitDevelopmentSingleVoterPreservesDefaultRf3AndRejectsConflictingTopology()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var original = fixture.Options;
        var single = original with
        {
            Peers = [original.PublicEndpoint],
            DevelopmentSingleVoter = true,
            AllowLoopbackHttp = true
        };
        single.Validate();
        var replica = single.CreateReplicaConfiguration(single.DataDirectory);
        await Assert.That(replica.VoterIds.Length).IsEqualTo(SingleVoter);
        await Assert.That(replica.Majority).IsEqualTo(SingleVoter);
        await Assert.That(replica.BenchmarkTopology).IsFalse();
        await Assert.That(replica.DevelopmentSingleVoter).IsTrue();
        (single with { DevelopmentSingleVoter = false }).Validate();
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => (single with { BenchmarkTopology = true }).Validate());
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => (single with { AllowLoopbackHttp = false }).Validate());
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => (original with { DevelopmentSingleVoter = true }).Validate());
        (replica with { DevelopmentSingleVoter = false }).Validate();
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => (replica with { BenchmarkTopology = true }).Validate());
        original.Validate();
        var rf3 = original.CreateReplicaConfiguration(original.DataDirectory);
        await Assert.That(rf3.Majority).IsEqualTo(Rf3Majority);
        await Assert.That(rf3.DevelopmentSingleVoter).IsFalse();
        await Assert.That(rf3.BenchmarkTopology).IsFalse();
    }
}
