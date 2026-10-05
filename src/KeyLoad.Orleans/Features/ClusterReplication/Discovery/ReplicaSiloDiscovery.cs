using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Describes a ready voter runtime generation without granting append, vote or commit authority.</summary>
/// <param name="VoterId">The configured voter identity.</param>
/// <param name="ClusterId">The fixed replica cluster identity.</param>
/// <param name="Incarnation">The database incarnation served by this silo.</param>
/// <param name="SiloAddress">The actual Orleans address including runtime generation.</param>
/// <param name="TransportReady">Whether the local node-owned endpoint has attached this silo transport.</param>
/// <param name="ApplicationRpcVersion">The transient request interface version; missing fields are incompatible.</param>
/// <param name="PeerEnvelopeVersion">The authenticated replica envelope version.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeWireContracts.DiscoveryAlias)]
public sealed record ReplicaSiloDiscovery(
    [property: global::Orleans.Id(0)] string VoterId,
    [property: global::Orleans.Id(1)] string ClusterId,
    [property: global::Orleans.Id(2)] Guid Incarnation,
    [property: global::Orleans.Id(3)] string SiloAddress,
    [property: global::Orleans.Id(4)] bool TransportReady,
    [property: global::Orleans.Id(5)] int ApplicationRpcVersion = 0,
    [property: global::Orleans.Id(6)] int PeerEnvelopeVersion = 0);

/// <summary>Publishes the actual local Orleans runtime generation after early service initialization.</summary>
/// <param name="configurationOptions">The local voter and current database incarnation.</param>
/// <param name="peerOptions">The fixed cluster identity used in discovery documents.</param>
/// <param name="localSilo">The Orleans runtime details containing the true silo generation.</param>
public sealed class ReplicaSiloDiscoveryState(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> peerOptions,
    ILocalSiloDetails localSilo)
{
    private readonly ReplicaConfiguration configuration = configurationOptions.Value;
    private readonly ReplicaPeerOptions options = peerOptions.Value;
    private int ready;

    /// <summary>Full local address including the actual runtime generation.</summary>
    /// <value>The canonical Orleans address for this silo process.</value>
    public string RuntimeAddress { get; } = localSilo.SiloAddress.ToParsableString();

    /// <summary>Returns a bounded description of the currently initialized silo generation.</summary>
    /// <returns>The local voter, cluster, incarnation, runtime address and readiness state.</returns>
    public ReplicaSiloDiscovery Read() => new(configuration.LocalId, options.ClusterId, configuration.Incarnation,
        RuntimeAddress, Volatile.Read(ref ready) != 0, GrainRoutingProtocol.RequestInterfaceVersion,
        ReplicaTransportProtocol.Version);

    /// <summary>Marks the per-silo replica endpoint available without waiting for a quorum.</summary>
    /// <remarks>The node-local endpoint must already be attached before this state is published.</remarks>
    public void MarkTransportReady() => Volatile.Write(ref ready, 1);

    /// <summary>Withdraws discovery after membership has finished its shutdown.</summary>
    /// <remarks>The later lifecycle callback drains endpoint maintenance while Orleans transport remains active.</remarks>
    public void StopDiscovery() => Volatile.Write(ref ready, 0);
}
