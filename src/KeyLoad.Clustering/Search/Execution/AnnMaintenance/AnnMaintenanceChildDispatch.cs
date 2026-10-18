using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class AnnMaintenanceChildDispatch
{
    internal static async Task<TResult> ExecuteAsync<TResult>(DecodedGrainRequest parent,
        Func<string, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics,
        IResourceLifetimeWorkBinding binding, byte[] payload, GrainReadKind? read,
        OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
    {
        var actorId = Guid.NewGuid();
        string signed;
        try
        {
            token.ThrowIfCancellationRequested();
            codec.ValidateScope(parent.Envelope);
            GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
            signed = codec.CreateAnnMaintenanceChild(actorId, parent.Envelope.PrincipalId!, read,
                command, commandId, payload, parent.Envelope.ExpiresAt);
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
                }, services, codec, binding, payload, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && command is not null)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, AnnMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
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
