using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Adapts native silo RPC to the original bounded node-local movement receiver.</summary>
internal sealed class PartitionMovementNativeReceiver(PartitionMovementRuntime runtime,
    PartitionMovementPeerAdmission admission, PartitionMovementReceiver receiver, OrleansNode node,
    IOptions<NodeOptions> options, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> limits, TimeProvider clock,
    IPartitionMovementNativeObserver? observationObserver = null)
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
        var armed = observationObserver?.Begin(OriginalNonce(request), options.Value.PublicEndpoint);
        var observation = armed is null ? null : new NativeObservation(armed);
        var entered = false;
        observation?.Enter(PartitionMovementNativeObservationStage.RuntimeAdmission);
        try
        {
            await runtime.RunAsync(async token =>
            {
                entered = true;
                reply = await ExecuteOwnedAsync(request, observeFailure, observation, token).ConfigureAwait(false);
                observation?.OperationCompleted = true;
            }, cancellationToken).ConfigureAwait(false);
            return reply ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.Unavailable);
        }
        catch (KeyLoadException error)
        {
            observation?.Fail(error, error.Code);
            if (entered && observation is { OperationCompleted: true })
            { observation.Enter(PartitionMovementNativeObservationStage.RuntimeCompletion); }
            return Failure(request, false, error.Code);
        }
        catch (OperationCanceledException error)
        {
            observation?.Fail(error, ErrorCode.Cancelled);
            if (entered && observation is { OperationCompleted: true })
            { observation.Enter(PartitionMovementNativeObservationStage.RuntimeCompletion); }
            return Failure(request, false, ErrorCode.Cancelled);
        }
        catch (Exception error)
        {
            observation?.Fail(error, null);
            if (entered && observation is { OperationCompleted: true })
            { observation.Enter(PartitionMovementNativeObservationStage.RuntimeCompletion); }
            throw;
        }
        finally
        {
            CompleteObservation(armed, observation?.Error, observation?.Authenticated ?? false,
                observation?.Dispatched ?? false, observation?.Failure, observation?.SecondaryFailure, observation?.FailureStage);
        }
    }

    private static Guid OriginalNonce(PartitionMovementGrainServiceRequest? request)
        => request?.Nonce ?? Guid.Empty;

    private async Task<PartitionMovementGrainServiceReply> ExecuteOwnedAsync(PartitionMovementGrainServiceRequest request,
        Action<Exception> observeFailure, NativeObservation? observation, CancellationToken token)
    {
        var authenticated = false;
        var dispatched = false;
        Action<PartitionMovementNativeObservationStage>? observeStage = observation is null ? null : observation.Enter;
        try
        {
            var reply = await PartitionMovementNativeLifetime.WithinLifetimeAsync(this, async bounded =>
            {
                request = await admission.VerifyNativeAsync(request, receiver.Discovery(),
                    (voter, address, caller) => node.BorrowPartitionMovementTransport().RequireCallerAsync(
                        voter, address, observeStage, caller), bounded, observeStage).ConfigureAwait(false);
                authenticated = true;
                observation?.Authenticated = true;
                admission.RequireNativeExpiryReplay(request, observeStage, bounded);
                return await PartitionMovementNativeLifetime.WithinExpiryAsync(this, request.ExpiresAt, inner =>
                {
                    inner.ThrowIfCancellationRequested();
                    dispatched = true;
                    observation?.Dispatched = true;
                    observation?.Enter(PartitionMovementNativeObservationStage.InnerDispatch);
                    return DispatchAsync(request, inner);
                }, bounded).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            observation?.Enter(PartitionMovementNativeObservationStage.OuterSigning);
            return SignOuter(reply, request);
        }
        catch (KeyLoadException error)
        {
            observation?.Fail(error, error.Code);
            return Failure(request, authenticated, error.Code, observation);
        }
        catch (OperationCanceledException error)
        {
            var code = dispatched && !request.Query ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled;
            observation?.Fail(error, code);
            return Failure(request, authenticated, code, observation);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var code = dispatched && !request.Query ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
            observation?.Fail(error, code);
            observeFailure(error);
            return Failure(request, authenticated, code, observation);
        }
    }

    private static void CompleteObservation(IPartitionMovementNativeObservation? observation, ErrorCode? error,
        bool authenticated, bool dispatched, Exception? original, Exception? secondary,
        PartitionMovementNativeObservationStage? failureStage)
    {
        if (observation is null)
        { return; }
        try
        { observation.Complete(error, authenticated, dispatched, original, secondary, failureStage); }
        catch (Exception observationFailure)
        {
            if (original is not null)
            {
                ServerFailureObserver.ThrowIfAny(secondary is null
                    ? [original, observationFailure] : [original, secondary, observationFailure]);
            }
            throw;
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

    private PartitionMovementGrainServiceReply Failure(PartitionMovementGrainServiceRequest? request, bool authenticated,
        ErrorCode code, NativeObservation? observation = null)
    {
        var reply = new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty, code,
            PartitionMovementProtocol.Unavailable, request?.Nonce ?? Guid.Empty, string.Empty);
        try
        { return authenticated && request is not null ? SignOuter(reply, request) : reply; }
        catch (Exception)
        {
            observation?.Enter(PartitionMovementNativeObservationStage.OuterSigning);
            throw;
        }
    }

    private PartitionMovementGrainServiceReply SignOuter(PartitionMovementGrainServiceReply reply, PartitionMovementGrainServiceRequest request)
    {
        reply = reply with { Nonce = request.Nonce, SenderSignature = string.Empty };
        using var mac = admission.CreateControlMac();
        return reply with { SenderSignature = mac.SignNativeReply(reply) };
    }
    private sealed class NativeObservation(IPartitionMovementNativeObservation observer)
    {
        internal bool Authenticated { get; set; }
        internal bool Dispatched { get; set; }
        internal bool OperationCompleted { get; set; }
        internal ErrorCode? Error { get; private set; }
        internal Exception? Failure { get; private set; }
        internal Exception? SecondaryFailure { get; private set; }
        internal PartitionMovementNativeObservationStage? FailureStage { get; private set; }
        private PartitionMovementNativeObservationStage stage;
        internal void Enter(PartitionMovementNativeObservationStage value)
        {
            stage = value;
            observer.Enter(value);
        }
        internal void Fail(Exception original, ErrorCode? code)
        {
            if (Failure is null)
            {
                Error = code;
                Failure = original;
                FailureStage = stage;
            }
            else if (!ReferenceEquals(Failure, original))
            { SecondaryFailure ??= original; }
        }
    }

}
