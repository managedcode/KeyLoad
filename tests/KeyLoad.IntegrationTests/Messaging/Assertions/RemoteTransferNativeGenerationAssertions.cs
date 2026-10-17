using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeGenerationAssertions
{
    internal static void Require(RemoteModelNativeRequest original,
        IReadOnlyList<ReplicaSiloDiscovery> signedSource, IReadOnlyList<ReplicaSiloDiscovery> signedDestination)
    {
        RequireGroup(original.SourceOwner, signedSource);
        RequireGroup(original.DestinationOwner, signedDestination);
        var caller = signedSource.Single(value => value.VoterId == original.CallerVoter);
        var destination = signedDestination.Single(value => value.VoterId == original.DestinationVoter);
        if (caller.SiloAddress != original.CallerSiloAddress
            || destination.SiloAddress != original.DestinationSiloAddress
            || caller.ClusterId != destination.ClusterId
            || caller.SiloAddress == destination.SiloAddress)
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
    }

    private static void RequireGroup(PhysicalShardRecord owner, IReadOnlyList<ReplicaSiloDiscovery> signed)
    {
        if (signed.Count != owner.VoterIds.Length || signed.Count != RequestCqrsRf3Protocol.NodeCount
            || signed.Select(value => value.VoterId).Distinct(StringComparer.Ordinal).Count() != signed.Count
            || signed.Select(value => value.SiloAddress).Distinct(StringComparer.Ordinal).Count() != signed.Count
            || signed.Select(value => value.ClusterId).Distinct(StringComparer.Ordinal).Count() != 1
            || signed.Any(value => !value.TransportReady || value.Incarnation != owner.Incarnation
                || !owner.VoterIds.Contains(value.VoterId, StringComparer.Ordinal)
                || SiloAddress.FromParsableString(value.SiloAddress).ToParsableString() != value.SiloAddress))
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
    }
}
