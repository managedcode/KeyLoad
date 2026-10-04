using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Orleans;

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
}
