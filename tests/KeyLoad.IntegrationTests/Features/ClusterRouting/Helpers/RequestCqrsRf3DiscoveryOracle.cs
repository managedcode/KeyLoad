using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3DiscoveryOracle
{
    internal static async Task<ReplicaSiloDiscovery[]> VerifyMixedImagesAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var old = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, RequestCqrsRf3Protocol.Node3,
            profile, cancellationToken).ConfigureAwait(false);
        await Assert.That(old.ApplicationRpcVersion).IsEqualTo(0);
        await Assert.That(old.PeerEnvelopeVersion).IsEqualTo(0);
        var current = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.CurrentMajority];
        for (var index = 0; index < current.Length; index++)
        {
            current[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
            await AssertCurrentAsync(current[index]).ConfigureAwait(false);
        }
        await Assert.That(current[0].SiloAddress).IsNotEqualTo(current[1].SiloAddress);
        await Assert.That(old.SiloAddress).IsNotEqualTo(current[0].SiloAddress);
        await Assert.That(old.SiloAddress).IsNotEqualTo(current[1].SiloAddress);
        return current;
    }

    internal static async Task<ReplicaSiloDiscovery[]> CaptureCurrentAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var observations = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < observations.Length; index++)
        {
            observations[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
            await AssertCurrentAsync(observations[index]).ConfigureAwait(false);
        }
        return observations;
    }

    internal static async Task AssertCurrentAsync(ReplicaSiloDiscovery observation)
    {
        await Assert.That(observation.ApplicationRpcVersion)
            .IsEqualTo(RequestCqrsRf3Protocol.ApplicationProtocolVersion);
        await Assert.That(observation.PeerEnvelopeVersion)
            .IsEqualTo(RequestCqrsRf3Protocol.PeerEnvelopeVersion);
    }

    internal static async Task AssertReplacementAsync(ReplicaSiloDiscovery[] prior,
        ReplicaSiloDiscovery[] current)
    {
        await Assert.That(current.Length).IsEqualTo(prior.Length);
        for (var index = 0; index < prior.Length; index++)
        {
            await AssertCurrentAsync(current[index]).ConfigureAwait(false);
            await Assert.That(current[index].VoterId).IsEqualTo(prior[index].VoterId);
            await Assert.That(current[index].ClusterId).IsEqualTo(prior[index].ClusterId);
            await Assert.That(current[index].Incarnation).IsEqualTo(prior[index].Incarnation);
            await Assert.That(current[index].SiloAddress).IsNotEqualTo(prior[index].SiloAddress);
        }
    }

    internal static async Task AssertOneReplacementAsync(ReplicaSiloDiscovery[] prior,
        ReplicaSiloDiscovery[] current, int replacedIndex)
    {
        await Assert.That(current.Length).IsEqualTo(prior.Length);
        for (var index = 0; index < prior.Length; index++)
        {
            await AssertCurrentAsync(current[index]).ConfigureAwait(false);
            await Assert.That(current[index].VoterId).IsEqualTo(prior[index].VoterId);
            await Assert.That(current[index].ClusterId).IsEqualTo(prior[index].ClusterId);
            await Assert.That(current[index].Incarnation).IsEqualTo(prior[index].Incarnation);
            await Assert.That(current[index].SiloAddress != prior[index].SiloAddress).IsEqualTo(index == replacedIndex);
        }
    }
}
