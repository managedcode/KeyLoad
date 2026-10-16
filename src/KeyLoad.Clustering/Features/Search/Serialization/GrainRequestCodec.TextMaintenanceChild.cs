namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateTextMaintenanceChild(Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, byte[] payload, DateTimeOffset expiry)
    {
        var context = LifetimeReadContext;
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
        return Issue(envelope);
    }
}
