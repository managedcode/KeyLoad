using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Validates identity fields only after the discovery signature covers the exact native bytes.</summary>
internal static class ReplicaDiscoveryIdentity
{
    internal static ReplicaDiscoveryObservation CreateObservation(string voterId, ReplicaSiloDiscovery? discovered,
        ReplicaConfiguration configuration, ReplicaPeerOptions options, TimeProvider clock)
    {
        if (discovered is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var address = ValidateDiscovery(voterId, discovered, configuration, options);
        var compatible = discovered.ApplicationRpcVersion == GrainRoutingProtocol.RequestInterfaceVersion
            && discovered.PeerEnvelopeVersion == ReplicaTransportProtocol.Version;
        return new(address, discovered.ApplicationRpcVersion, discovered.PeerEnvelopeVersion,
            compatible, discovered.TransportReady, clock.GetTimestamp(), discovered.RuntimeJournalReaderContract);
    }

    private static SiloAddress ValidateDiscovery(string voterId, ReplicaSiloDiscovery discovered,
        ReplicaConfiguration configuration, ReplicaPeerOptions options)
    {
        if (discovered.VoterId != voterId || discovered.ClusterId != options.ClusterId
            || discovered.Incarnation != configuration.Incarnation
            || string.IsNullOrWhiteSpace(discovered.SiloAddress)
            || discovered.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        SiloAddress address;
        try
        {
            address = SiloAddress.FromParsableString(discovered.SiloAddress);
        }
        catch (FormatException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }

        if (address.Endpoint.Port <= 0 || address.ToParsableString() != discovered.SiloAddress)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }

        return address;
    }
}
