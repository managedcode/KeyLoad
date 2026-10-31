using KeyLoad.Clustering.Search;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal sealed class OnlineTextChildCalls(DecodedGrainRequest parent, GrainRequestEnvelope parentEnvelope,
    Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
    IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics,
    IGrainContext context, IResourceLifetimeWorkBinding binding)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();

    internal Task<OnlineTextCapabilityResult> CapabilityAsync(OnlineTextIndexMaintenanceRequest request,
        OnlineTextCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? acknowledged = null)
        => DispatchAsync<OnlineTextCapabilityResult>((actor, principal) => codec.CreateOnlineTextCapability(
            actor, principal, new(SessionId, request, kind, page, intent, acknowledged)),
            Guid.Empty, token);

    internal Task<ProjectionBatch> ReadPageAsync(ReadProjectionBatchRequest request, CancellationToken token)
        => DispatchAsync<ProjectionBatch>((actor, principal) => codec.CreateOnlineTextProjectionRead(actor,
            principal, request), Guid.Empty, token);

    internal Task<ProjectionBatchResult> CheckpointAsync(CommitProjectionBatchRequest request, CancellationToken token)
        => DispatchAsync<ProjectionBatchResult>((actor, principal) => codec.CreateOnlineTextCheckpoint(actor,
            principal, request), request.CommandId, token);

    internal Task<OnlineTextIndexMaintenanceResult> PublishAsync(Command<DatabaseCommand> original, CancellationToken token)
        => DispatchAsync<OnlineTextIndexMaintenanceResult>((actor, _) => codec.CreateOnlineTextPublication(actor,
            original), original.CommandId, token);

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
        var verified = await executeChild(parentEnvelope, token).ConfigureAwait(true);
        if (verified.Error is { } failure)
        {
            throw Errors.Fail(failure, verified.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
    }

    private Task<TResult> DispatchAsync<TResult>(Func<Guid, string, GrainRequestEnvelope> sign,
        Guid commandId, CancellationToken token) where TResult : class
        => MaintenanceChildDispatch.CallAsync<TResult>(parent, sign, commandId, executeChild,
            services, codec, diagnostics, binding, TextIndexMaintenanceProtocol.Interrupted, token);
}
