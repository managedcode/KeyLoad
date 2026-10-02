using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Resolves the current persisted identity through the native signed authentication read.</summary>
internal static class DatabaseCredentialResolver
{
    /// <summary>Fetches owned canonical principal bytes without publishing an operation execution identity.</summary>
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
            JsonDefaults.Serialize(key[ServerProtocol.BearerPrefix.Length..]));
        var reply = await context.RequestServices.GetRequiredService<OrleansNode>()
            .ExecuteAsync(requestId, token, context.RequestAborted).ConfigureAwait(false);
        return reply.Payload;
    }
}
