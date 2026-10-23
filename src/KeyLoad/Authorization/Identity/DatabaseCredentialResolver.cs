using KeyLoad.Clustering;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Server;

/// <summary>Resolves the current persisted identity through the execution authentication read.</summary>
internal static class DatabaseCredentialResolver
{
    /// <summary>Fetches the owned execution principal envelope without publishing an operation execution identity.</summary>
    /// <param name="context">The actual authenticated public request and services.</param>
    /// <param name="executionObserver">The optional borrowed execution observer; no credentials are exposed.</param>
    /// <returns>The current persisted principal reply, decoded by the owning adapter.</returns>
    internal static async Task<ReadOnlyMemory<byte>> ReadAsync(HttpContext context,
        IDatabaseAuthenticationExecutionObserver? executionObserver = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = context.Request.Headers.Authorization.ToString();
        if (!key.StartsWith(ServerProtocol.BearerPrefix, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var requestId = Guid.NewGuid();
        var codec = context.RequestServices.GetRequiredService<GrainRequestCodec>();
        var envelope = CanonicalCredentialDispatch.Prepare(codec,
            key[ServerProtocol.BearerPrefix.Length..], requestId);
        RequestFailureDiagnostic.MarkCredentialDispatch(context);
        var started = DatabasePhaseTelemetry.Begin();
        var outcome = DatabasePhaseOutcome.Faulted;
        GrainOperationReply reply;
        var observedExecution = false;
        try
        {
            var node = context.RequestServices.GetRequiredService<OrleansNode>();
            using var connection = ServerConnectionFeature.Acquire(context, context.RequestAborted);
            reply = await CanonicalCredentialDispatch.ReadAsync(node, envelope, connection, requestId, () =>
            {
                if (executionObserver is not null)
                {
                    executionObserver.Started(context.TraceIdentifier, requestId);
                    observedExecution = true;
                }
            });
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
            if (observedExecution)
            {
                executionObserver!.Terminal(context.TraceIdentifier, requestId, outcome);
            }
        }
        return reply.Payload;
    }
}
