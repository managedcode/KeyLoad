using KeyLoad.Replication;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Publishes the actual local Orleans runtime generation after early service initialization.</summary>
/// <param name="configurationOptions">The local voter and current database incarnation.</param>
/// <param name="peerOptions">The fixed cluster identity used in discovery documents.</param>
/// <param name="localSilo">The Orleans runtime details containing the true silo generation.</param>
/// <param name="runtimeJournalReaderContract">The current reader capability verified from both physical stores; zero is unsupported.</param>
public sealed class ReplicaSiloDiscoveryState(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> peerOptions,
    ILocalSiloDetails localSilo, int runtimeJournalReaderContract = StoreReaderContract.Unspecified)
{
    internal const int UnadvertisedProtocolVersion = 0;
    private const int ReadEmptyRead = 0;
    private const int MarkTransportReadyValueSingleItemCount = 1;
    private const int StopDiscoveryValueEmptyCount = 0;

    private readonly ReplicaConfiguration configuration = configurationOptions.Value;
    private readonly ReplicaPeerOptions options = peerOptions.Value;
    private int ready;

    /// <summary>Full local address including the actual runtime generation.</summary>
    /// <value>The canonical Orleans address for this silo process.</value>
    public string RuntimeAddress { get; } = localSilo.SiloAddress.ToParsableString();

    /// <summary>Returns a bounded description of the currently initialized silo generation.</summary>
    /// <returns>The local voter, cluster, incarnation, runtime address and readiness state.</returns>
    public ReplicaSiloDiscovery Read() => new(configuration.LocalId, options.ClusterId, configuration.Incarnation,
        RuntimeAddress, Volatile.Read(ref ready) != ReadEmptyRead, GrainRoutingProtocol.RequestInterfaceVersion,
        ReplicaTransportProtocol.Version, runtimeJournalReaderContract);

    /// <summary>Marks the per-silo replica endpoint available without waiting for a quorum.</summary>
    /// <remarks>The node-local endpoint must already be attached before this state is published.</remarks>
    public void MarkTransportReady() => Volatile.Write(ref ready, MarkTransportReadyValueSingleItemCount);

    /// <summary>Withdraws discovery after membership has finished its shutdown.</summary>
    /// <remarks>The later lifecycle callback drains endpoint maintenance while Orleans transport remains active.</remarks>
    public void StopDiscovery() => Volatile.Write(ref ready, StopDiscoveryValueEmptyCount);
}
