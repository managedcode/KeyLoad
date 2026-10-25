namespace KeyLoad.Clustering;

/// <summary>Issues bounded maintenance children with each original purpose and expiry law.</summary>
internal static class SearchMaintenanceRequestIssuance
{
    internal static GrainRequestEnvelope CreateAnnMaintenanceChild(GrainRequestCodec codec, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, byte[] payload, DateTimeOffset expiry)
    {
        var context = codec.LifetimeReadContext;
        if (expiry <= context.Now || expiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var envelope = new GrainRequestEnvelope
        {
            Purpose = AnnMaintenanceChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Scope.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload),
            ExpiresAt = expiry
        };
        _ = AnnMaintenanceChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
    internal static GrainRequestEnvelope CreateTextMaintenanceChild(GrainRequestCodec codec, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, byte[] payload, DateTimeOffset expiry)
    {
        var context = codec.LifetimeReadContext;
        if (expiry <= context.Now || expiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var envelope = new GrainRequestEnvelope
        {
            Purpose = TextMaintenanceChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Scope.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload),
            ExpiresAt = expiry
        };
        _ = TextMaintenanceChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
}
