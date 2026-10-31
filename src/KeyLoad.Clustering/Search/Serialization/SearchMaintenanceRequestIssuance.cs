namespace KeyLoad.Clustering;

/// <summary>Issues bounded maintenance children under their parent's child purpose and expiry law.</summary>
internal static class SearchMaintenanceRequestIssuance
{
    internal static GrainRequestEnvelope CreateChild(GrainRequestCodec codec, string purpose, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, object payload, DateTimeOffset expiry)
    {
        var context = codec.LifetimeReadContext;
        if (expiry <= context.Now || expiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return codec.Issue(new GrainRequestEnvelope
        {
            Purpose = purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Scope.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload),
            ExpiresAt = expiry
        });
    }
}
