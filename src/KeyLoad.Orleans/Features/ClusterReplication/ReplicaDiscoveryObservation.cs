namespace KeyLoad.Orleans;

/// <summary>One authenticated runtime-generation observation for a fixed configured voter.</summary>
internal sealed class ReplicaDiscoveryObservation(
    SiloAddress address,
    int applicationRpcVersion,
    int peerEnvelopeVersion,
    bool protocolCompatible,
    bool transportReady,
    long observedTimestamp)
{
    internal SiloAddress Address { get; } = address;
    internal int ApplicationRpcVersion { get; } = applicationRpcVersion;
    internal int PeerEnvelopeVersion { get; } = peerEnvelopeVersion;
    internal bool ProtocolCompatible { get; } = protocolCompatible;
    internal bool TransportReady { get; } = transportReady;
    internal long ObservedTimestamp { get; } = observedTimestamp;
    internal bool Compatible => ProtocolCompatible && TransportReady;
}
