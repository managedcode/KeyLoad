using System.Collections.Immutable;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class IndependentSiloReplicaTopologyTests
{
    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    [Arguments(3, 2)]
    [Arguments(5, 3)]
    [Arguments(5, 5)]
    public async Task SiloMembershipAndPhysicalAcknowledgementAreIndependent(int silos, int replicas)
    {
        var deployment = new ClusterDeploymentOptions { SiloCount = silos, ReplicaCount = replicas };
        await Assert.That(deployment.IsValid()).IsTrue();
        var names = ClusterTopologyNodeNames.Read(null, null, false, silos);
        await Assert.That(names.Length).IsEqualTo(silos);
        using var fixture = new PartitionHostRecoveryFixture();
        var original = fixture.Options;
        var peers = Enumerable.Range(1, replicas).Select(index => $"https://node{index}").ToImmutableArray();
        var node = original with { PublicEndpoint = peers[0], SiloAddress = "node1", Peers = peers };
        node.Validate();
        var configuration = node.CreateReplicaConfiguration(node.DataDirectory);
        configuration.Validate();
        await Assert.That(configuration.VoterIds.Length).IsEqualTo(replicas);
        await Assert.That(configuration.Majority).IsEqualTo(replicas / 2 + 1);
        await Assert.That(configuration.VoterIds.Length <= names.Length).IsTrue();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(2, 3)]
    [Arguments(49, 3)]
    public async Task InvalidDeploymentIsRejectedBeforeResources(int silos, int replicas)
    {
        var deployment = new ClusterDeploymentOptions { SiloCount = silos, ReplicaCount = replicas };
        await Assert.That(deployment.IsValid()).IsFalse();
    }
}
