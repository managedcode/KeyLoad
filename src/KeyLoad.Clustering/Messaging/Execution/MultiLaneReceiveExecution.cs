using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class MultiLaneReceiveExecution
{
    internal static async Task<MultiLaneReceiveResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        ILogger diagnostics, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var request = GrainPayload.ReadCommand<MultiLaneReceiveRequest>(parent.Payload);
        ArgumentNullException.ThrowIfNull(binding);
        var incarnation = codec.Incarnation;
        MultiLaneReceiveAdmission.Validate(request, parent.Envelope.CommandId,
            services.GetRequiredService<IOptions<MessagingExecutionOptions>>().Value, token);
        var outcomes = ImmutableArray.CreateBuilder<QueueLaneReceiveOutcome>(request.Requests.Length);
        var halted = false;
        KeyLoadException? stopped = null;
        foreach (var leaf in request.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (!halted)
            {
                stopped = (ValidateNext(parent, incarnation, codec, clock, outcomes.Count, diagnostics)).Value;
                halted = stopped is not null;
            }
            if (halted)
            {
                outcomes.Add(new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted));
                continue;
            }
            var dispatched = await DispatchAsync(leaf, executeChild, services, codec, clock,
                diagnostics, parent, binding, incarnation, outcomes.Count, token).ConfigureAwait(true);
            stopped = dispatched.Stopped;
            var outcome = stopped is null ? Outcome(leaf, dispatched.Reply!, diagnostics, parent.Envelope.RequestId)
                : new QueueLaneReceiveOutcome(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted);
            outcomes.Add(outcome);
            halted = stopped is not null || outcome.Status == QueueLaneReceiveStatus.Unknown;
        }
        token.ThrowIfCancellationRequested();
        return new MultiLaneReceiveResult(request.RequestId, outcomes.MoveToImmutable(), stopped?.Code, stopped?.Message);
    }

    private static async Task<MultiLaneReceiveDispatchResult> DispatchAsync(ReceiveRequest leaf,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec,
        TimeProvider clock, ILogger diagnostics, DecodedGrainRequest parent,
        IResourceLifetimeWorkBinding binding, Guid incarnation, int precedingOutcomes, CancellationToken token)
    {
        var actorId = Guid.NewGuid();
        var invoked = false;
        try
        {
            var child = codec.CreateMultiLaneReceiveChild(actorId, parent.Envelope.PrincipalId!, leaf.RequestId,
                leaf, parent.Envelope.ExpiresAt);
            var reply = await MaintenanceChildDispatch.ExecuteAsync(parent, child, actorId, leaf.RequestId,
                (request, cancellation) =>
                {
                    invoked = true;
                    return executeChild(request, cancellation);
                }, services, codec, binding, token).ConfigureAwait(true);
            return new(reply, null);
        }
        catch (KeyLoadException error) when (invoked)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId, GrainFailureStage.ReplyEncoding,
                ErrorCode.UnknownWriteOutcome);
            return new(new GrainOperationReply
            {
                Error = ErrorCode.UnknownWriteOutcome,
                SafeDetail = MultiLaneReceiveAdmission.Interrupted
            }, null);
        }
        catch (KeyLoadException error) when (!invoked && error.Code == ErrorCode.TokenInvalidated)
        {
            var stopped = (ValidateNext(parent, incarnation, codec, clock,
                precedingOutcomes, diagnostics)).Value;
            if (stopped is null)
            {
                throw;
            }
            return new(null, stopped);
        }
    }

    private static QueueLaneReceiveOutcome Outcome(ReceiveRequest leaf, GrainOperationReply reply,
        ILogger diagnostics, Guid parentId)
    {
        if (reply.Error is { } error)
        {
            var status = error is ErrorCode.UnknownWriteOutcome or ErrorCode.OwnershipLost or ErrorCode.Cancelled
                ? QueueLaneReceiveStatus.Unknown : QueueLaneReceiveStatus.Rejected;
            return new(leaf.RequestId, leaf.Lane, status, Error: error, SafeDetail: reply.SafeDetail);
        }
        try
        {
            var result = GrainReplyFactory.RequireValue(reply).Value as ReceiveResult
                ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, GrainRoutingProtocol.InvalidRequest);
            if (result.RequestId != leaf.RequestId)
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, GrainRoutingProtocol.InvalidRequest);
            }
            return new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.Committed, result);
        }
        catch (Exception payloadFailure) when (payloadFailure is KeyLoadException or JsonException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(diagnostics, payloadFailure, parentId, GrainFailureStage.ReplyEncoding,
                ErrorCode.UnknownWriteOutcome);
            return new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.Unknown,
                Error: ErrorCode.UnknownWriteOutcome, SafeDetail: MultiLaneReceiveAdmission.Interrupted);
        }
    }

    private static (bool HasValue, KeyLoadException? Value) ValidateNext(DecodedGrainRequest parent, Guid incarnation,
        GrainRequestCodec codec, TimeProvider clock, int precedingOutcomes, ILogger diagnostics)
    {
        var identityValidated = false;
        try
        {
            GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
            identityValidated = true;
            codec.ValidateScope(parent.Envelope);
            return (false, default(KeyLoadException?));
        }
        catch (KeyLoadException error) when (precedingOutcomes > 0)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.EnvelopeVerification, error.Code);
            if (identityValidated && error.Code == ErrorCode.TokenInvalidated
                && parent.Envelope.Incarnation == incarnation
                && parent.Envelope.ExpiresAt <= clock.GetUtcNow())
            {
                // Only the verified expired parent stopped this undispatched suffix.
                return (true, ((KeyLoadException?)(error)));
            }
            // Identity or other parent-scope failure cannot safely release retained payloads.
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, MultiLaneReceiveAdmission.Interrupted);
        }
    }

}
