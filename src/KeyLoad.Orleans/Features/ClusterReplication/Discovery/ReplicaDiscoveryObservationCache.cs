using System.Collections.Concurrent;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Retains at most one bounded fresh observation for each configured voter.</summary>
internal sealed class ReplicaDiscoveryObservationCache
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, ReplicaDiscoveryObservation> observations;

    internal ReplicaDiscoveryObservationCache(IOptions<ReplicaConfiguration> configurationOptions, ReplicaSiloDiscoveryState local,
        TimeProvider clock)
    {
        var configuration = configurationOptions.Value;
        this.configuration = configurationOptions.Value;
        this.local = local;
        this.clock = clock;
        observations = new(configuration.VoterIds.Length, configuration.VoterIds.Length, StringComparer.Ordinal);
    }

    internal ReplicaDiscoveryObservation ReadLocal()
    {
        var current = local.Read();
        var address = SiloAddress.FromParsableString(current.SiloAddress);
        if (address.Endpoint.Port <= 0 || address.ToParsableString() != current.SiloAddress)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var protocolCompatible = current.ApplicationRpcVersion == GrainRoutingProtocol.RequestInterfaceVersion
            && current.PeerEnvelopeVersion == ReplicaTransportProtocol.Version;
        var observation = new ReplicaDiscoveryObservation(address, current.ApplicationRpcVersion,
            current.PeerEnvelopeVersion, protocolCompatible, current.TransportReady, clock.GetTimestamp());
        observations[configuration.LocalId] = observation;
        return observation;
    }

    internal SiloAddress ResolveLocal() => RequireCompatible(ReadLocal());

    internal bool TryFresh(string voterId, out ReplicaDiscoveryObservation? observation)
    {
        observation = null;
        if (!observations.TryGetValue(voterId, out var found))
        {
            return false;
        }

        if (clock.GetElapsedTime(found.ObservedTimestamp) >= configuration.LowerElectionTimeout)
        {
            ((ICollection<KeyValuePair<string, ReplicaDiscoveryObservation>>)observations)
                .Remove(new(voterId, found));
            return false;
        }

        observation = found;
        return true;
    }

    internal void Remove(string voterId) => observations.TryRemove(voterId, out _);

    internal void Store(string voterId, ReplicaDiscoveryObservation observation)
        => observations[voterId] = observation;

    internal static SiloAddress RequireCompatible(ReplicaDiscoveryObservation observation)
    {
        if (!observation.ProtocolCompatible)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.IncompatibleCohort);
        }

        if (!observation.TransportReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        return observation.Address;
    }
}
