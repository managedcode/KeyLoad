namespace KeyLoad.Clustering;

/// <summary>Issues bounded maintenance children with each original purpose and expiry law.</summary>
internal static class SearchMaintenanceRequestIssuance
{
    internal static string CreateAnnMaintenanceChild(GrainRequestCodec codec, Guid requestId, string principalId,
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
            Incarnation = context.Database.Store.Identity.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, context.Database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
        _ = AnnMaintenanceChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
    internal static string CreateAnnProjectionJobChild(GrainRequestCodec codec, Guid requestId, string principalId,
        Guid commandId, byte[] payload, DateTimeOffset expiry)
    {
        var context = codec.LifetimeReadContext;
        if (expiry <= context.Now)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var envelope = new GrainRequestEnvelope
        {
            Purpose = AnnProjectionJobChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Database.Store.Identity.Incarnation,
            ReadKind = null,
            CommandKind = OperationKind.MaintainAnnIndex,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, context.Database.Limits.MaxBatchBytes),
            ExpiresAt = expiry < context.Now + context.Lifetime ? expiry : context.Now + context.Lifetime
        };
        _ = AnnProjectionJobChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
    internal static string CreateTextMaintenanceChild(GrainRequestCodec codec, Guid requestId, string principalId,
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
            Incarnation = context.Database.Store.Identity.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, context.Database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
        _ = TextMaintenanceChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
}
