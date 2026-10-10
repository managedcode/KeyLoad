using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal sealed class AnnMaintenanceChildCalls(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
    IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, ILogger diagnostics)
{
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal DatabaseEngine Database => services.GetRequiredService<DatabaseEngine>();

    internal Task<AnnMaintenanceCapabilityResult> CapabilityAsync(AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null)
        => ReadAsync<AnnMaintenanceCapabilityRequest, AnnMaintenanceCapabilityResult>(GrainReadKind.AnnMaintenance,
            new(request, SessionId, kind, page, intent), token);

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), kind, null, Guid.Empty, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), null, kind, commandId, token);

    internal Task AbortAsync() => services.GetRequiredService<INativeAnnMaintenance>().AbortAsync(SessionId);

    private async Task<TResult> DispatchAsync<TResult>(byte[] payload, GrainReadKind? read,
        OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var principal = GrainRequestAuthority.ReloadForRequest(Database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var actorId = Guid.NewGuid();
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, actorId, commandId, token, connectionId: connectionId);
        var signed = command is { } kind ? codec.CreateCommand(actorId, principal.Id, kind, commandId, payload)
            : codec.CreateRead(actorId, principal.Id, read!.Value, payload);
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await GrainRequestStreamConsumer.DrainAsync(cancellation =>
            {
                invoked = true;
                return executeChild(signed, cancellation);
            }, serializer, actorId, clock, routing, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && command is not null)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, AnnMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        { throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest); }
        try
        {
            return NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TResult
                ?? throw Errors.Fail(command is null ? ErrorCode.Corruption : ErrorCode.UnknownWriteOutcome,
                    AnnMaintenanceProtocol.Interrupted);
        }
        catch (Exception error) when (command is not null && error is KeyLoadException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.ReplyEncoding, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, AnnMaintenanceProtocol.Interrupted);
        }
    }
}
