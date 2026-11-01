using KeyLoad.Clustering;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.Server;

/// <summary>Resolves the current persisted identity through the execution authentication read.</summary>
internal static class DatabaseCredentialResolver
{
    /// <summary>Authenticates through the credential's user grain without publishing an operation execution identity.</summary>
    /// <param name="context">The actual authenticated public request and services.</param>
    /// <param name="executionObserver">The optional borrowed execution observer; no credentials are exposed.</param>
    /// <returns>The current persisted principal reply, decoded by the owning adapter.</returns>
    internal static async Task<GrainValue> ReadAsync(HttpContext context,
        IDatabaseAuthenticationExecutionObserver? executionObserver = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = context.Request.Headers.Authorization.ToString();
        if (!key.StartsWith(ServerProtocol.BearerPrefix, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var requestId = Guid.NewGuid();
        RequestFailureDiagnostic.MarkCredentialDispatch(context);
        var started = DatabasePhaseTelemetry.Begin();
        var outcome = DatabasePhaseOutcome.Faulted;
        PrincipalRecord principal;
        var observedExecution = false;
        try
        {
            var node = context.RequestServices.GetRequiredService<OrleansNode>();
            using var connection = ServerConnectionFeature.Acquire(context, context.RequestAborted);
            principal = await CanonicalCredentialDispatch.AuthenticateAsync(node, key[ServerProtocol.BearerPrefix.Length..],
                connection, requestId, () =>
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
        return new GrainValue(principal);
    }
}
