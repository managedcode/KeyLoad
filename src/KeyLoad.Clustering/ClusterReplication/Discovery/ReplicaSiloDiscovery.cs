using KeyLoad.Storage;

namespace KeyLoad.Clustering;

/// <summary>Describes a ready voter runtime generation without granting append, vote or commit authority.</summary>
/// <param name="VoterId">The configured voter identity.</param>
/// <param name="ClusterId">The fixed replica cluster identity.</param>
/// <param name="Incarnation">The database incarnation served by this silo.</param>
/// <param name="SiloAddress">The actual Orleans address including runtime generation.</param>
/// <param name="TransportReady">Whether the local node-owned endpoint has attached this silo transport.</param>
/// <param name="ApplicationRpcVersion">The transient request interface version; missing fields are incompatible.</param>
/// <param name="PeerEnvelopeVersion">The authenticated replica envelope version.</param>
/// <param name="RuntimeJournalReaderContract">The current capability verified from both node-local stores.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeWireContracts.DiscoveryAlias)]
public sealed record ReplicaSiloDiscovery(
    [property: global::Orleans.Id(0)] string VoterId,
    [property: global::Orleans.Id(1)] string ClusterId,
    [property: global::Orleans.Id(2)] Guid Incarnation,
    [property: global::Orleans.Id(3)] string SiloAddress,
    [property: global::Orleans.Id(4)] bool TransportReady,
    [property: global::Orleans.Id(5)] int ApplicationRpcVersion = ReplicaSiloDiscoveryState.UnadvertisedProtocolVersion,
    [property: global::Orleans.Id(6)] int PeerEnvelopeVersion = ReplicaSiloDiscoveryState.UnadvertisedProtocolVersion,
    [property: global::Orleans.Id(7)] int RuntimeJournalReaderContract = StoreReaderContract.Unspecified);
