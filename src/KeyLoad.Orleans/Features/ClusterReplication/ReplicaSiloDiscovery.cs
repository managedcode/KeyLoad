using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Describes a ready voter runtime generation without granting append, vote or commit authority.</summary>
/// <param name="VoterId">The configured voter identity.</param>
/// <param name="ClusterId">The fixed replica cluster identity.</param>
/// <param name="Incarnation">The database incarnation served by this silo.</param>
/// <param name="SiloAddress">The actual Orleans address including runtime generation.</param>
/// <param name="TransportReady">Whether the local node-owned endpoint has attached this silo transport.</param>
public sealed record ReplicaSiloDiscovery(string VoterId, string ClusterId, Guid Incarnation,
    string SiloAddress, bool TransportReady);

/// <summary>Publishes the actual local Orleans runtime generation after early service initialization.</summary>
/// <param name="configuration">The local voter and current database incarnation.</param>
/// <param name="options">The fixed cluster identity used in discovery documents.</param>
/// <param name="localSilo">The Orleans runtime details containing the true silo generation.</param>
public sealed class ReplicaSiloDiscoveryState(ReplicaConfiguration configuration, ReplicaPeerOptions options,
    ILocalSiloDetails localSilo)
{
    private int ready;

    /// <summary>Full local address including the actual runtime generation.</summary>
    /// <value>The canonical Orleans address for this silo process.</value>
    public string RuntimeAddress { get; } = localSilo.SiloAddress.ToParsableString();

    /// <summary>Returns a bounded description of the currently initialized silo generation.</summary>
    /// <returns>The local voter, cluster, incarnation, runtime address and readiness state.</returns>
    public ReplicaSiloDiscovery Read() => new(configuration.LocalId, options.ClusterId, configuration.Incarnation,
        RuntimeAddress, Volatile.Read(ref ready) != 0);

    /// <summary>Marks the per-silo replica endpoint available without waiting for a quorum.</summary>
    /// <remarks>The node-local endpoint must already be attached before this state is published.</remarks>
    public void MarkTransportReady() => Volatile.Write(ref ready, 1);

    /// <summary>Withdraws discovery after membership has finished its shutdown.</summary>
    /// <remarks>The later lifecycle callback drains endpoint maintenance while Orleans transport remains active.</remarks>
    public void StopDiscovery() => Volatile.Write(ref ready, 0);
}
