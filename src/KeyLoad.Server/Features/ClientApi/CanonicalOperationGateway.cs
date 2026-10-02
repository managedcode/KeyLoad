using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Shares authenticated signed request-actor dispatch across public protocol adapters.</summary>
internal static class CanonicalOperationGateway
{
    /// <summary>Executes one operation using the current persisted middleware principal.</summary>
    /// <param name="context">The current authenticated HTTP request, never a retained session.</param>
    /// <param name="readKind">The read capability, or null for a command.</param>
    /// <param name="commandKind">The command capability, or null for a read.</param>
    /// <param name="commandId">The caller's stable write identity, empty for reads.</param>
    /// <param name="payload">The canonical typed payload.</param>
    /// <param name="cancellationToken">Cancels the caller's wait without asserting write rollback.</param>
    /// <returns>The exact canonical reply and actual execution identity.</returns>
    internal static async Task<DispatchedOperationReply> ExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord principal)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var requestId = Guid.NewGuid();
        var codec = context.RequestServices.GetRequiredService<GrainRequestCodec>();
        var signed = readKind is { } read ? codec.CreateRead(requestId, principal.Id, read, payload)
            : codec.CreateCommand(requestId, principal.Id, commandKind!.Value, commandId, payload);
        OperationResponseHeaders.Publish(context, requestId);
        var reply = await context.RequestServices.GetRequiredService<OrleansNode>()
            .ExecuteAsync(requestId, signed, commandKind.HasValue, cancellationToken).ConfigureAwait(false);
        return new(requestId, reply.Payload);
    }

    /// <summary>Returns the actual operation identity, or null before actor dispatch began.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The request-local operation GUID if an execution started.</returns>
    internal static Guid? RequestId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OperationResponseHeaders.RequestId(context);
    }
}
