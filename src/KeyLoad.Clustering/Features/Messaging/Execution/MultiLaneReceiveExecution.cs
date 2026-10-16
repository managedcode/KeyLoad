using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal static class MultiLaneReceiveExecution
{
    private const int NoPrecedingOutcomes = 0;
    internal static async Task<MultiLaneReceiveResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var request = GrainNativePayload.ReadCommand<MultiLaneReceiveRequest>(parent.Payload);
        ArgumentNullException.ThrowIfNull(binding);
        var database = services.GetRequiredService<DatabaseEngine>();
        MultiLaneReceiveAdmission.Validate(request, parent.Envelope.CommandId,
            services.GetRequiredService<IOptions<MessagingExecutionOptions>>().Value, database.Limits, token);
        var principal = GrainRequestAuthority.Reload(database, parent.Envelope.PrincipalId!, clock);
        var outcomes = ImmutableArray.CreateBuilder<QueueLaneReceiveOutcome>(request.Requests.Length);
        var halted = false;
        KeyLoadException? stopped = null;
        foreach (var leaf in request.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (!halted)
            {
                stopped = ValidateNext(parent, database, codec, clock, outcomes.Count, diagnostics);
                halted = stopped is not null;
            }
            if (halted)
            {
                AcceptOutcome(request.RequestId, outcomes,
                    new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted), stopped,
                    binding, database.Limits.MaxBatchBytes, token);
                continue;
            }
            var dispatched = await DispatchAsync(principal, leaf, executeChild, services, codec, clock,
                serializer, routing, diagnostics, parent, binding, outcomes.Count, token).ConfigureAwait(true);
            stopped = dispatched.Stopped;
            var outcome = stopped is null ? Outcome(leaf, dispatched.Reply!, diagnostics, parent.Envelope.RequestId)
                : new QueueLaneReceiveOutcome(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted);
            AcceptOutcome(request.RequestId, outcomes, outcome, stopped, binding, database.Limits.MaxBatchBytes, token);
            halted = stopped is not null || outcome.Status == QueueLaneReceiveStatus.Unknown;
        }
        token.ThrowIfCancellationRequested();
        var result = new MultiLaneReceiveResult(request.RequestId, outcomes.MoveToImmutable(), stopped?.Code, stopped?.Message);
        CheckResult(result, binding, database.Limits.MaxBatchBytes, token);
        return result;
    }

    private static async Task<MultiLaneReceiveDispatchResult> DispatchAsync(PrincipalRecord principal, ReceiveRequest leaf,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec,
        TimeProvider clock, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, DecodedGrainRequest parent,
        IResourceLifetimeWorkBinding binding, int precedingOutcomes, CancellationToken token)
    {
        var actorId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(leaf);
        var invoked = false;
        var transferred = false;
        try
        {
            var signed = codec.CreateMultiLaneReceiveChild(actorId, principal.Id, leaf.RequestId,
                payload, parent.Envelope.ExpiresAt);
            transferred = true;
            var reply = await MaintenanceChildDispatch.ExecuteAsync(parent, signed, actorId, leaf.RequestId,
                (request, cancellation) =>
                {
                    invoked = true;
                    return executeChild(request, cancellation);
                }, services, codec, clock, serializer, routing, binding, payload, token).ConfigureAwait(true);
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
            var stopped = ValidateNext(parent, services.GetRequiredService<DatabaseEngine>(), codec, clock,
                precedingOutcomes, diagnostics);
            if (stopped is null)
            {
                throw;
            }
            return new(null, stopped);
        }
        finally
        {
            if (!transferred)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static void AcceptOutcome(Guid requestId, ImmutableArray<QueueLaneReceiveOutcome>.Builder outcomes,
        QueueLaneReceiveOutcome next, KeyLoadException? stopped, IResourceLifetimeWorkBinding binding,
        int maximumBytes, CancellationToken token)
    {
        var candidate = new MultiLaneReceiveResult(requestId, outcomes.ToImmutable().Add(next),
            stopped?.Code, stopped?.Message);
        CheckResult(candidate, binding, maximumBytes, token);
        outcomes.Add(next);
    }

    private static void CheckResult(MultiLaneReceiveResult result, IResourceLifetimeWorkBinding binding,
        int maximumBytes, CancellationToken token)
    {
        // Encoding the retained terminal result does not admit another command after parent expiry.
        token.ThrowIfCancellationRequested();
        using var counter = new ResultByteCounterStream(binding.ResultRetention.AvailableResultBytes(maximumBytes),
            token.ThrowIfCancellationRequested);
        System.Text.Json.JsonSerializer.Serialize(counter, result, JsonDefaults.Options);
        token.ThrowIfCancellationRequested();
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
            var result = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as ReceiveResult
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

    private static KeyLoadException? ValidateNext(DecodedGrainRequest parent, DatabaseEngine database,
        GrainRequestCodec codec, TimeProvider clock, int precedingOutcomes, ILogger diagnostics)
    {
        var identityValidated = false;
        try
        {
            GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
            identityValidated = true;
            codec.ValidateScope(parent.Envelope);
            return null;
        }
        catch (KeyLoadException error) when (precedingOutcomes > NoPrecedingOutcomes)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.EnvelopeVerification, error.Code);
            if (identityValidated && error.Code == ErrorCode.TokenInvalidated
                && parent.Envelope.Incarnation == database.Store.Identity.Incarnation
                && parent.Envelope.ExpiresAt <= clock.GetUtcNow())
            {
                // Only the verified expired parent stopped this undispatched suffix.
                return error;
            }
            // Identity or other parent-scope failure cannot safely release retained payloads.
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, MultiLaneReceiveAdmission.Interrupted);
        }
    }

}

internal sealed record MultiLaneReceiveDispatchResult(GrainOperationReply? Reply, KeyLoadException? Stopped);
