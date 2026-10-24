using KeyLoad.Clustering.Search;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class OnlineTextChildCalls(DecodedGrainRequest parent, GrainRequestEnvelope parentEnvelope,
    Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
    IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics,
    OnlineTextFrameAccounting frames, IGrainContext context, IResourceLifetimeWorkBinding binding)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal DatabaseLimits Limits => services.GetRequiredService<IOptions<DatabaseLimits>>().Value;

    internal Task<OnlineTextCapabilityResult> CapabilityAsync(OnlineTextIndexMaintenanceRequest request,
        OnlineTextCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? acknowledged = null)
        => DispatchAsync<OnlineTextCapabilityResult>((actor, principal) => codec.CreateOnlineTextCapability(
            actor, principal, new(SessionId, request, kind, page, intent, acknowledged), parent.Envelope.ExpiresAt),
            Guid.Empty, token);

    internal Task<ProjectionBatch> ReadPageAsync(ReadProjectionBatchRequest request, CancellationToken token)
        => DispatchAsync<ProjectionBatch>((actor, principal) => codec.CreateOnlineTextProjectionRead(actor,
            principal, request, parent.Envelope.ExpiresAt), Guid.Empty, token);

    internal Task<ProjectionBatchResult> CheckpointAsync(CommitProjectionBatchRequest request, CancellationToken token)
        => DispatchAsync<ProjectionBatchResult>((actor, principal) => codec.CreateOnlineTextCheckpoint(actor,
            principal, request, parent.Envelope.ExpiresAt), request.CommandId, token);

    internal Task<OnlineTextIndexMaintenanceResult> PublishAsync(Command<DatabaseCommand> original, CancellationToken token)
        => DispatchAsync<OnlineTextIndexMaintenanceResult>((actor, _) => codec.CreateOnlineTextPublication(actor,
            original, parent.Envelope.ExpiresAt), original.CommandId, token);

    internal Task AbortAsync() => abort(SessionId);

    internal async Task ObserveCapturedAsync(CancellationToken token)
    {
        if (!codec.HasPhaseObserver)
        {
            return;
        }
        if (parent.Envelope.CommandKind != OperationKind.MaintainOnlineTextIndex
            || parent.Envelope.CommandId == Guid.Empty || parent.Envelope.ReadKind is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        await codec.ObservePhaseAsync(parent, GrainRequestPhase.OnlineTextCaptured, context, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        frames.AdmitChildExchange();
        var verified = await executeChild(parentEnvelope, token).ConfigureAwait(true);
        if (verified.Error is { } failure)
        {
            throw Errors.Fail(failure, verified.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
    }

    private async Task<TResult> DispatchAsync<TResult>(Func<Guid, string, GrainRequestEnvelope> sign,
        Guid commandId, CancellationToken token) where TResult : class
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var actorId = Guid.NewGuid();
        var child = sign(actorId, parent.Envelope.PrincipalId!);
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await MaintenanceChildDispatch.ExecuteAsync(parent, child, actorId, commandId,
                (value, cancellation) =>
                {
                    frames.AdmitChildExchange();
                    invoked = true;
                    return executeChild(value, cancellation);
                }, services, codec, binding, null, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && commandId != Guid.Empty)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        return OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TResult
            ?? throw Errors.Fail(commandId == Guid.Empty ? ErrorCode.Corruption : ErrorCode.UnknownWriteOutcome,
                TextIndexMaintenanceProtocol.Interrupted);
    }
}
