using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal sealed class TextMaintenanceChildCalls(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
    IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, ILogger diagnostics, IResourceLifetimeWorkBinding binding)
{
    private readonly Func<INativeTextMaintenance> borrowNative = services.GetRequiredService<Func<INativeTextMaintenance>>();
    internal Guid ParentRequestId => parent.Envelope.RequestId;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal DatabaseEngine Database => services.GetRequiredService<DatabaseEngine>();

    internal Task<TextMaintenanceCapabilityResult> CapabilityAsync(TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind kind, CancellationToken token)
        => CapabilityAsync(request, kind, null, null, null, token);

    internal Task<TextMaintenanceCapabilityResult> CapabilityAsync(TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind kind, ProjectionBatch? page,
        CommitProjectionBatchRequest? intent, ProjectionBatchResult? acknowledged, CancellationToken token)
        => ReadAsync<TextMaintenanceCapabilityRequest, TextMaintenanceCapabilityResult>(GrainReadKind.TextMaintenance,
            new(SessionId, request, kind, page, intent, acknowledged), token);

    internal Task<TResult> ReadAsync<TRequest, TResult>(GrainReadKind kind, TRequest request, CancellationToken token)
        where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), kind, null, Guid.Empty, token);

    internal Task<TResult> CommandAsync<TRequest, TResult>(OperationKind kind, TRequest request,
        Guid commandId, CancellationToken token) where TResult : class
        => DispatchAsync<TResult>(NativeSerialization.Serialize(request), null, kind, commandId, token);

    internal Task AbortAsync() => borrowNative().AbortAsync(SessionId);

    private async Task<TResult> DispatchAsync<TResult>(byte[] payload, GrainReadKind? read,
        OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var principal = GrainRequestAuthority.ReloadForRequest(Database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var actorId = Guid.NewGuid();
        string signed;
        try
        {
            signed = codec.CreateTextMaintenanceChild(actorId, principal.Id, read, command,
                commandId, payload, parent.Envelope.ExpiresAt);
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw;
        }
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await MaintenanceChildDispatch.ExecuteAsync(parent, signed, actorId, commandId,
                (value, cancellation) =>
                {
                    invoked = true;
                    return executeChild(value, cancellation);
                }, services, codec, clock, serializer, routing, binding, payload, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && command is not null)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        try
        {
            return NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TResult
                ?? throw Errors.Fail(command is null ? ErrorCode.Corruption : ErrorCode.UnknownWriteOutcome,
                    TextIndexMaintenanceProtocol.Interrupted);
        }
        catch (Exception error) when (command is not null && error is KeyLoadException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.ReplyEncoding, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
    }
}
