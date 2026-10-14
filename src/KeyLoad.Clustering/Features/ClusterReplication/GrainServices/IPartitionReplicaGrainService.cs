using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Clustering;

/// <summary>One directly addressed replica protocol endpoint per Orleans silo.</summary>
[Alias(ReplicaTransportProtocol.ServiceAlias), Version(ReplicaTransportProtocol.InterfaceVersion)]
public interface IPartitionReplicaGrainService : IGrainService
{
    /// <summary>Executes a bounded authenticated request and returns a signed exact-byte result.</summary>
    /// <param name="request">The authenticated envelope addressed to this silo generation.</param>
    /// <param name="cancellationToken">Cancellation for transport readiness and local dispatch.</param>
    /// <returns>A signed reply bound to the request, including a typed safe error when dispatch fails.</returns>
    [Alias(ReplicaTransportProtocol.ExchangeAlias)]
    Task<ReplicaPeerReply> ExchangeAsync(ReplicaPeerEnvelope request, CancellationToken cancellationToken);
    /// <summary>Returns authenticated initialized metadata from the exact current native silo generation.</summary>
    /// <param name="request">The signed fixed-voter caller, destination, nonce and bounded expiry.</param>
    /// <param name="cancellationToken">Cancellation of the joined native metadata callback.</param>
    /// <returns>A signed exact native discovery payload or bounded typed error.</returns>
    [Alias(ReplicaNativeDiscoveryProtocol.DiscoverAlias)]
    Task<ReplicaNativeDiscoveryReply> DiscoverAsync(ReplicaNativeDiscoveryRequest request, CancellationToken cancellationToken);
}
