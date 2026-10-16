namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateMultiLaneReceiveChild(Guid requestId, string principalId, Guid commandId,
        byte[] payload, DateTimeOffset expiry)
    {
        var context = LifetimeReadContext;
        if (expiry <= context.Now || expiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var envelope = new GrainRequestEnvelope
        {
            Purpose = MultiLaneReceiveChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Database.Store.Identity.Incarnation,
            CommandKind = OperationKind.Receive,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, context.Database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
        _ = MultiLaneReceiveChildScope.Validate(envelope);
        return Issue(envelope);
    }
}
