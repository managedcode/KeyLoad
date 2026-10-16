using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Per-silo protocol adapter; durable state belongs to the injected node-local endpoint.</summary>
/// <param name="id">The Orleans grain-service identity for this silo instance.</param>
/// <param name="silo">The hosting Orleans silo.</param>
/// <param name="loggerFactory">Factory used by the Orleans grain-service base type.</param>
/// <param name="services">The actual silo provider; storage-backed services resolve only during native initialization.</param>
/// <param name="diagnostics">Operational logging for unexpected endpoint failures.</param>
public sealed class PartitionReplicaGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    IServiceProvider services, ILogger<PartitionReplicaGrainService> diagnostics)
    : GrainService(id, silo, loggerFactory), IPartitionReplicaGrainService
{
    private const int FailureEventId = 3;
    private static readonly Action<ILogger, ReplicaRpc?, Exception?> LogFailure = LoggerMessage.Define<ReplicaRpc?>(LogLevel.Error,
        new EventId(FailureEventId), ReplicaTransportProtocol.EndpointFailureLog);

    private IReplicaEndpoint endpoint = null!;
    private ReplicaSiloDiscoveryState discovery = null!;
    private ReplicaEnvelopeAuthenticator authentication = null!;
    private ReplicaNativeDiscoveryRuntime nativeDiscovery = null!;
    private IReplicaTransportObservation? observation;

    /// <summary>Attaches transport during RuntimeGrainServices without waiting for consensus membership.</summary>
    /// <param name="serviceProvider">The active silo service provider.</param>
    /// <returns>A task that completes after the endpoint is attached and discovery is marked ready.</returns>
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        var lifecycle = services.GetRequiredService<ReplicaTransportLifecycle>();
        endpoint = services.GetRequiredService<Func<IReplicaEndpoint>>()();
        discovery = services.GetRequiredService<ReplicaSiloDiscoveryState>();
        authentication = services.GetRequiredService<Func<ReplicaEnvelopeAuthenticator>>()();
        nativeDiscovery = services.GetRequiredService<ReplicaNativeDiscoveryRuntime>();
        lifecycle.Attach(endpoint, discovery, nativeDiscovery);
        observation = services.GetService<IReplicaTransportObservation>();
        endpoint.AttachTransport(services.GetRequiredService<ReplicaGrainServiceClient>());
        discovery.MarkTransportReady();
    }

    /// <summary>Returns a signed typed error instead of sending an unregistered exception across Orleans.</summary>
    /// <param name="request">The peer-authenticated operation envelope.</param>
    /// <param name="cancellationToken">Cancellation for readiness and node-local processing.</param>
    /// <returns>A signed success or typed failure reply bound to <paramref name="request"/>.</returns>
    public async Task<ReplicaPeerReply> ExchangeAsync(ReplicaPeerEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            request = authentication.VerifyRequest(request);
            var completed = observation?.BeginIncoming(request.Method);
            await endpoint.TransportReady.WaitAsync(cancellationToken).ConfigureAwait(true);
            var result = await endpoint.HandleAsync(request.Method, request.Payload, cancellationToken).ConfigureAwait(true);
            if (result.Length > authentication.MaximumPayloadBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
            }

            var reply = authentication.CreateReply(request, result);
            completed?.Invoke(request.Payload, result);
            return reply;
        }
        catch (KeyLoadException error)
        {
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, error.Code, error.Message);
        }
        catch (OperationCanceledException)
        {
            var forwardedWrite = request.Method == ReplicaRpc.Forward;
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty,
                forwardedWrite ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost,
                forwardedWrite ? ReplicaProtocol.InterruptedWrite : ReplicaProtocol.NoLeader);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            LogFailure(diagnostics, request?.Method, null);
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.OwnershipLost, ReplicaTransportProtocol.EndpointFailure);
        }
    }

    /// <summary>Discovers actual initialized silo metadata without depending on replication quorum.</summary>
    /// <param name="request">The signed fixed-voter caller and exact addressed native generation.</param>
    /// <param name="cancellationToken">Cancellation joined by the native metadata callback owner.</param>
    /// <returns>The original signed typed metadata reply task.</returns>
    public Task<ReplicaNativeDiscoveryReply> DiscoverAsync(ReplicaNativeDiscoveryRequest request, CancellationToken cancellationToken)
        => nativeDiscovery.DiscoverAsync(request, cancellationToken);

    /// <summary>Consensus remains available until the later RuntimeStorageServices shutdown callback.</summary>
    /// <returns>The base grain-service shutdown task.</returns>
    public override Task Stop() => base.Stop();
}
