using KeyLoad.Core;
using KeyLoad.Orleans.Features.Search;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal sealed class OnlineTextChildCalls(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
    IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, ILogger diagnostics, OnlineTextFrameAccounting frames, global::Orleans.Runtime.IGrainContext context)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal DatabaseEngine Database => services.GetRequiredService<DatabaseEngine>();

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

    internal Task<OnlineTextIndexMaintenanceResult> PublishAsync(ReplicatedOperation original, CancellationToken token)
        => DispatchAsync<OnlineTextIndexMaintenanceResult>((actor, _) => codec.CreateOnlineTextPublication(actor,
            original, parent.Envelope.ExpiresAt), original.Id, token);

    internal Task AbortAsync() => services.GetRequiredService<INativeOnlineTextMaintenance>().AbortAsync(SessionId);

    internal async Task ObserveCapturedAsync(CancellationToken token)
    {
        if (!codec.HasPhaseObserver)
        { return; }
        if (parent.Envelope.CommandKind != OperationKind.MaintainOnlineTextIndex
            || parent.Envelope.CommandId == Guid.Empty || parent.Envelope.ReadKind is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        await codec.ObservePhaseAsync(parent, GrainRequestPhase.OnlineTextCaptured, context, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(Database, parent.Envelope, clock));
    }

    private async Task<TResult> DispatchAsync<TResult>(Func<Guid, string, string> sign,
        Guid commandId, CancellationToken token) where TResult : class
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var principal = GrainRequestAuthority.ReloadForRequest(Database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var actorId = Guid.NewGuid();
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, actorId, commandId, token,
            connectionId: connectionId);
        var signed = sign(actorId, principal.Id);
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await GrainRequestStreamConsumer.DrainAsync(cancellation =>
            {
                invoked = true;
                var original = executeChild(signed, cancellation);
                return frames.ObserveChild(original, cancellation);
            }, serializer, actorId, clock, routing, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && commandId != Guid.Empty)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        { throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest); }
        return NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TResult
            ?? throw Errors.Fail(commandId == Guid.Empty ? ErrorCode.Corruption : ErrorCode.UnknownWriteOutcome,
                TextIndexMaintenanceProtocol.Interrupted);
    }
}
