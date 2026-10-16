using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

public sealed partial class PartitionReplicaGrainService
{
    private bool RequiresOwnedForward()
        => services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(Func<NativeRequestWorkOwner>))
            || endpoint is IReplicaAuthenticatedForwardExchange { RequiresOwnedForwardWork: true };

    private async Task<ReplicaPeerReply> ExchangeForwardAsync(ReplicaPeerEnvelope original, CancellationToken cancellationToken)
    {
        // Authentication precedes native owner creation. Failures after owner creation never use the old outer catches.
        ReplicaPeerEnvelope request;
        try
        {
            request = authentication.VerifyRequest(original);
        }
        catch (KeyLoadException error)
        {
            return authentication.CreateReply(original, ReadOnlyMemory<byte>.Empty, error.Code, error.Message);
        }
        var selected = endpoint as IReplicaAuthenticatedForwardExchange
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
        var completed = observation?.BeginIncoming(request.Method);
        await endpoint.TransportReady.WaitAsync(cancellationToken).ConfigureAwait(true);
        return await selected.HandleAuthenticatedForwardExchangeAsync(request.Payload,
            (payload, error) => CreateForwardReply(request, payload, error), NativeSerialization.Measure,
            reply => completed?.Invoke(request.Payload, reply.Payload), cancellationToken).ConfigureAwait(true);
    }

    private ReplicaPeerReply CreateForwardReply(ReplicaPeerEnvelope request, ReadOnlyMemory<byte> payload, Exception? error)
    {
        if (error is KeyLoadException domain)
        {
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty, domain.Code, domain.Message);
        }
        if (error is OperationCanceledException)
        {
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty,
                ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite);
        }
        if (error is not null)
        {
            if (!GrainBoundaryErrors.Handles(error))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
            LogFailure(diagnostics, request.Method, null);
            return authentication.CreateReply(request, ReadOnlyMemory<byte>.Empty,
                ErrorCode.OwnershipLost, ReplicaTransportProtocol.EndpointFailure);
        }
        if (payload.Length > authentication.MaximumPayloadBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
        }
        return authentication.CreateReply(request, payload);
    }
}
