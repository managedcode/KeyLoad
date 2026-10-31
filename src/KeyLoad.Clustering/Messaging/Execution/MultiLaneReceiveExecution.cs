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
        IServiceProvider services, GrainRequestCodec codec,
        ILogger diagnostics, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var request = GrainPayload.ReadCommand<MultiLaneReceiveRequest>(parent.Payload);
        ArgumentNullException.ThrowIfNull(binding);
        MultiLaneReceiveAdmission.Validate(request, parent.Envelope.CommandId,
            services.GetRequiredService<IOptions<MessagingExecutionOptions>>().Value, token);
        var outcomes = ImmutableArray.CreateBuilder<QueueLaneReceiveOutcome>(request.Requests.Length);
        var halted = false;
        foreach (var leaf in request.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (halted)
            {
                outcomes.Add(new(leaf.RequestId, leaf.Lane, QueueLaneReceiveStatus.NotAttempted));
                continue;
            }
            ValidateParent(parent, codec, outcomes.Count, diagnostics);
            var reply = await DispatchAsync(leaf, executeChild, services, codec, diagnostics, parent, binding, token)
                .ConfigureAwait(true);
            var outcome = Outcome(leaf, reply, diagnostics, parent.Envelope.RequestId);
            outcomes.Add(outcome);
            halted = outcome.Status == QueueLaneReceiveStatus.Unknown;
        }
        token.ThrowIfCancellationRequested();
        return new MultiLaneReceiveResult(request.RequestId, outcomes.MoveToImmutable());
    }

    private static async Task<GrainOperationReply> DispatchAsync(ReceiveRequest leaf,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics, DecodedGrainRequest parent,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var actorId = Guid.NewGuid();
        var invoked = false;
        try
        {
            var child = codec.CreateMultiLaneReceiveChild(actorId, parent.Envelope.PrincipalId!, leaf.RequestId, leaf);
            return await MaintenanceChildDispatch.ExecuteAsync(parent, child, actorId, leaf.RequestId,
                (request, cancellation) =>
                {
                    invoked = true;
                    return executeChild(request, cancellation);
                }, services, codec, binding, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId, GrainFailureStage.ReplyEncoding,
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

    private static void ValidateParent(DecodedGrainRequest parent, GrainRequestCodec codec, int precedingOutcomes,
        ILogger diagnostics)
    {
        try
        {
            GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
            codec.ValidateScope(parent.Envelope);
        }
        catch (KeyLoadException error) when (precedingOutcomes > 0)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.EnvelopeVerification, error.Code);
            // A parent-scope failure after an earlier lane cannot safely release retained payloads.
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, MultiLaneReceiveAdmission.Interrupted);
        }
    }
}
