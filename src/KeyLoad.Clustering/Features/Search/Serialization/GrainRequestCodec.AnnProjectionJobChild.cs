namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateAnnProjectionJobChild(Guid requestId, string principalId,
        Guid commandId, byte[] payload, DateTimeOffset expiry)
    {
        var context = LifetimeReadContext;
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
        return Issue(envelope);
    }
}
