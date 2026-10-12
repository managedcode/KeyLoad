using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Adapts native silo RPC to the original bounded node-local movement receiver.</summary>
internal sealed class PartitionMovementNativeReceiver(PartitionMovementRuntime runtime,
    PartitionMovementPeerAdmission admission, PartitionMovementReceiver receiver, OrleansNode node,
    IOptions<NodeOptions> options, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    internal PartitionMovementPeerAdmission Admission => admission;
    internal PartitionMovementReceiver Receiver => receiver;
    internal IOptions<NodeOptions> Options => options;
    internal IOptions<GrainRoutingOptions> Routing => routing;
    internal IOptions<DatabaseLimits> Limits => limits;
    internal TimeProvider Clock => clock;

    internal async Task<PartitionMovementGrainServiceReply> ExchangeAsync(PartitionMovementGrainServiceRequest request,
        Action<Exception> observeFailure, CancellationToken cancellationToken)
    {
        PartitionMovementGrainServiceReply? reply = null;
        try
        {
            await runtime.RunAsync(async token =>
            { reply = await ExecuteOwnedAsync(request, observeFailure, token).ConfigureAwait(false); }, cancellationToken).ConfigureAwait(false);
            return reply ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.Unavailable);
        }
        catch (KeyLoadException error) { return Failure(request, false, error.Code); }
        catch (OperationCanceledException) { return Failure(request, false, ErrorCode.Cancelled); }
    }

    private async Task<PartitionMovementGrainServiceReply> ExecuteOwnedAsync(PartitionMovementGrainServiceRequest request,
        Action<Exception> observeFailure, CancellationToken token)
    {
        var authenticated = false;
        var dispatched = false;
        try
        {
            var reply = await PartitionMovementNativeLifetime.WithinLifetimeAsync(this, async bounded =>
            {
                request = await admission.VerifyNativeAsync(request, receiver.Discovery(),
                    node.BorrowPartitionMovementTransport().RequireCallerAsync, bounded).ConfigureAwait(false);
                authenticated = true;
                return await PartitionMovementNativeLifetime.WithinExpiryAsync(this, request.ExpiresAt, inner =>
                {
                    inner.ThrowIfCancellationRequested();
                    dispatched = true;
                    return DispatchAsync(request, inner);
                }, bounded).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return SignOuter(reply, request);
        }
        catch (KeyLoadException error) { return Failure(request, authenticated, error.Code); }
        catch (OperationCanceledException)
        { return Failure(request, authenticated, dispatched && !request.Query ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            observeFailure(error);
            return Failure(request, authenticated, dispatched && !request.Query ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost);
        }
    }

    private Task<PartitionMovementGrainServiceReply> DispatchAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken) => request.Channel switch
        {
            PartitionMovementGrainServiceChannel.Phase => PartitionMovementNativePhaseOperations.PhaseAsync(this, request, cancellationToken),
            PartitionMovementGrainServiceChannel.Outcome => PartitionMovementNativePhaseOperations.OutcomeAsync(this, request, cancellationToken),
            PartitionMovementGrainServiceChannel.ReceiverIssue => PartitionMovementNativeIssuanceOperations.ExecuteAsync(this, request, cancellationToken),
            PartitionMovementGrainServiceChannel.RetireCancellation => PartitionMovementNativeRetireOperations.ExecuteAsync(this, request, cancellationToken),
            PartitionMovementGrainServiceChannel.TransferData => PartitionMovementNativeTransferOperations.ExecuteAsync(this, request, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof)
        };

    private PartitionMovementGrainServiceReply Failure(PartitionMovementGrainServiceRequest? request, bool authenticated, ErrorCode code)
    {
        var reply = new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty, code,
            PartitionMovementProtocol.Unavailable, request?.Nonce ?? Guid.Empty, string.Empty);
        return authenticated && request is not null ? SignOuter(reply, request) : reply;
    }

    private PartitionMovementGrainServiceReply SignOuter(PartitionMovementGrainServiceReply reply, PartitionMovementGrainServiceRequest request)
    {
        reply = reply with { Nonce = request.Nonce, SenderSignature = string.Empty };
        using var mac = admission.CreateControlMac();
        return reply with { SenderSignature = mac.SignNativeReply(reply) };
    }
}
