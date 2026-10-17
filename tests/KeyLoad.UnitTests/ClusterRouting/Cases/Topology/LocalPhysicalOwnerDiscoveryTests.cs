using System.Collections.Immutable;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class LocalPhysicalOwnerDiscoveryTests
{
    [Test]
    public async Task OrdinaryGroupAdmitsOnlyItsConfiguredVotersAndUsesItsOwnSecret()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var options = fixture.Options;
        var configuration = options.CreateReplicaConfiguration(options.DataDirectory);
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        foreach (var voter in configuration.VoterIds)
        {
            var selected = PhysicalOwnerConfiguredTuples.ForVoter(options, configuration, voter);
            await Assert.That(selected.Owner).IsEqualTo(local.Owner);
        }
        await Assert.That(PartitionMovementNativeDiscoveryIdentity.Secret(options, configuration, local))
            .IsEqualTo(options.PeerSecret);
        await Assert.That(() => PhysicalOwnerConfiguredTuples.ForVoter(options, configuration, ReplicaSecurityFixture.UnknownVoter))
            .Throws<KeyLoadException>();
        var foreign = local with { Owner = local.Owner with { PhysicalShardId = Guid.NewGuid() } };
        await Assert.That(() => PartitionMovementNativeDiscoveryIdentity.Secret(options, configuration, foreign))
            .Throws<KeyLoadException>();
    }

    [Test]
    [Arguments(1, 5)]
    [Arguments(2, 2)]
    [Arguments(5, 1)]
    public async Task ExplicitForeignTrustUsesEachGroupsActualCopyCount(int localCopies, int foreignCopies)
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var peers = Enumerable.Range(1, localCopies).Select(index => $"https://local-{index}").ToImmutableArray();
        var trust = new PhysicalGroupTrustOptions
        {
            Role = PhysicalGroupTrustRole.Control,
            ForeignPhysicalShardId = Guid.NewGuid(),
            ForeignIncarnation = Guid.NewGuid(),
            ForeignPeerSecret = fixture.Options.SigningKey,
            ForeignVoterIds = Enumerable.Range(1, foreignCopies).Select(index => $"https://foreign-{index}").ToArray()
        };
        var options = fixture.Options with
        {
            PublicEndpoint = peers[0],
            Peers = peers,
            SiloAddress = "local-1",
            AllowPrivateNetworkHttp = true,
            PhysicalGroupTrust = trust
        };
        options.Validate();
        var configuration = options.CreateReplicaConfiguration(options.DataDirectory);
        var foreign = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        foreach (var voter in trust.ForeignVoterIds)
        {
            await Assert.That(PhysicalOwnerConfiguredTuples.ForVoter(options, configuration, voter).Owner)
                .IsEqualTo(foreign.Owner);
        }
        await Assert.That(PartitionMovementNativeDiscoveryIdentity.Secret(options, configuration, foreign))
            .IsEqualTo(trust.ForeignPeerSecret);
    }
}
