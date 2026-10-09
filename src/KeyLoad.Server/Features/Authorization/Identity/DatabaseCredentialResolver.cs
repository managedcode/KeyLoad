using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Resolves the current persisted identity through the native signed authentication read.</summary>
internal static class DatabaseCredentialResolver
{
    /// <summary>Fetches the owned native principal envelope without publishing an operation execution identity.</summary>
    /// <param name="context">The actual authenticated public request and services.</param>
    /// <returns>The current persisted principal reply, decoded by the owning adapter.</returns>
    internal static async Task<ReadOnlyMemory<byte>> ReadAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = context.Request.Headers.Authorization.ToString();
        if (!key.StartsWith(ServerProtocol.BearerPrefix, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential); }
        var requestId = Guid.NewGuid();
        var codec = context.RequestServices.GetRequiredService<GrainRequestCodec>();
        var token = codec.CreateRead(requestId, null, GrainReadKind.Authenticate,
            NativeSerialization.Serialize(key[ServerProtocol.BearerPrefix.Length..]));
        RequestFailureDiagnostic.MarkCredentialDispatch(context);
        var started = DatabasePhaseTelemetry.Begin();
        var outcome = DatabasePhaseOutcome.Faulted;
        GrainOperationReply reply;
        try
        {
            var node = context.RequestServices.GetRequiredService<OrleansNode>();
            using var connection = ServerConnectionFeature.Acquire(context, context.RequestAborted);
            using var requestContext = node.OpenRequestContext(null, requestId,
                Guid.Empty, connection.Token, connection.Id);
            reply = await node.ExecuteAsync(requestId, token, false, connection.Token).ConfigureAwait(false);
            outcome = DatabasePhaseOutcome.Completed;
        }
        catch (Exception failure) when (failure is OperationCanceledException or KeyLoadException { Code: ErrorCode.Cancelled })
        {
            outcome = DatabasePhaseTelemetry.CancellationOutcome(context.RequestAborted);
            throw;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.PublicAuthenticationDispatch, outcome, started);
        }
        return reply.Payload;
    }
}
