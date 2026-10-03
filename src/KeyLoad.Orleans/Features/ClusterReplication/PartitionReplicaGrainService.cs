using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Per-silo protocol adapter; durable state belongs to the injected node-local endpoint.</summary>
/// <param name="id">The Orleans grain-service identity for this silo instance.</param>
/// <param name="silo">The hosting Orleans silo.</param>
/// <param name="loggerFactory">Factory used by the Orleans grain-service base type.</param>
/// <param name="endpoint">Node-owned replica operations and transport readiness.</param>
/// <param name="discovery">Local runtime-generation discovery state.</param>
/// <param name="authentication">Peer-envelope authentication and bounded replay admission.</param>
/// <param name="diagnostics">Operational logging for unexpected endpoint failures.</param>
public sealed class PartitionReplicaGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    IReplicaEndpoint endpoint, ReplicaSiloDiscoveryState discovery, ReplicaEnvelopeAuthenticator authentication,
    ILogger<PartitionReplicaGrainService> diagnostics)
    : GrainService(id, silo, loggerFactory), IPartitionReplicaGrainService
{
    private const int FailureEventId = 3;
    private static readonly Action<ILogger, ReplicaRpc?, Exception?> LogFailure = LoggerMessage.Define<ReplicaRpc?>(LogLevel.Error,
        new EventId(FailureEventId), ReplicaTransportProtocol.EndpointFailureLog);

    /// <summary>Attaches transport during RuntimeGrainServices without waiting for consensus membership.</summary>
    /// <param name="serviceProvider">The active silo service provider.</param>
    /// <returns>A task that completes after the endpoint is attached and discovery is marked ready.</returns>
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        endpoint.AttachTransport(serviceProvider.GetRequiredService<ReplicaGrainServiceClient>());
        discovery.MarkTransportReady();
    }

    /// <summary>Returns a signed typed error instead of sending an unregistered exception across Orleans.</summary>
    /// <param name="request">The peer-authenticated operation envelope.</param>
    /// <param name="cancellationToken">Cancellation for readiness and node-local processing.</param>
    /// <returns>A signed success or typed failure reply bound to <paramref name="request"/>.</returns>
    public async Task<ReplicaPeerReply> ExchangeAsync(ReplicaPeerEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            request = authentication.VerifyRequest(request);
            await endpoint.TransportReady.WaitAsync(cancellationToken).ConfigureAwait(true);
            var result = await endpoint.HandleAsync(request.Method, request.Payload, cancellationToken).ConfigureAwait(true);
            if (result.Length > authentication.MaximumPayloadBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
            }

            return authentication.CreateReply(request, result);
        }
        catch (KeyLoadException error)
        {
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, error.Code, error.Message);
        }
        catch (OperationCanceledException)
        {
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            LogFailure(diagnostics, request?.Method, null);
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.OwnershipLost, ReplicaTransportProtocol.EndpointFailure);
        }
    }

    /// <summary>Consensus remains available until the later RuntimeStorageServices shutdown callback.</summary>
    /// <returns>The base grain-service shutdown task.</returns>
    public override Task Stop() => base.Stop();
}
