using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class MultiLaneReceiveExecution
{
    private const int NoPrecedingOutcomes = 0;
    internal static async Task<MultiLaneReceiveResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, CancellationToken token)
    {
        var request = GrainNativePayload.ReadCommand<MultiLaneReceiveRequest>(parent.Payload);
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
                outcomes.Add(new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted));
                continue;
            }
            var reply = await DispatchAsync(principal, leaf, executeChild, services, codec, clock,
                serializer, routing, diagnostics, parent.Envelope.RequestId, token).ConfigureAwait(true);
            var outcome = Outcome(leaf, reply, diagnostics, parent.Envelope.RequestId);
            outcomes.Add(outcome);
            halted = outcome.Status == QueueLaneReceiveStatus.Unknown;
        }
        token.ThrowIfCancellationRequested();
        return new(request.RequestId, outcomes.MoveToImmutable(), stopped?.Code, stopped?.Message);
    }

    private static async Task<GrainOperationReply> DispatchAsync(PrincipalRecord principal, ReceiveRequest leaf,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec,
        TimeProvider clock, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, Guid parentId, CancellationToken token)
    {
        var actorId = Guid.NewGuid();
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, actorId, leaf.RequestId, token, connectionId: connectionId);
        var signed = codec.CreateCommand(actorId, principal.Id, OperationKind.Receive, leaf.RequestId,
            NativeSerialization.Serialize(leaf));
        var invoked = false;
        try
        {
            return await GrainRequestStreamConsumer.DrainAsync(
                createStream: cancellation =>
                {
                    invoked = true;
                    return executeChild(signed, cancellation);
                },
                serializer: serializer, requestId: actorId, clock: clock, options: routing,
                cancellationToken: token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parentId, GrainFailureStage.ReplyEncoding,
                ErrorCode.UnknownWriteOutcome);
            return new GrainOperationReply
            {
                Error = ErrorCode.UnknownWriteOutcome,
                SafeDetail = MultiLaneReceiveAdmission.Interrupted
            };
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
            var result = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as ReceiveResult
                ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, GrainRoutingProtocol.InvalidRequest);
            if (result.RequestId != leaf.RequestId)
            { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, GrainRoutingProtocol.InvalidRequest); }
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
