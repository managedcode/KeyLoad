namespace KeyLoad.Clustering;

/// <summary>Issues the original bounded multi-lane receive child without changing its execution payload.</summary>
internal static class MultiLaneReceiveRequestIssuance
{
    internal static string CreateMultiLaneReceiveChild(GrainRequestCodec codec, Guid requestId, string principalId, Guid commandId,
        byte[] payload, DateTimeOffset expiry)
    {
        var context = codec.LifetimeReadContext;
        if (expiry <= context.Now || expiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var envelope = new GrainRequestEnvelope
        {
            Purpose = MultiLaneReceiveChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = context.Scope.Incarnation,
            CommandKind = OperationKind.Receive,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload, context.Scope.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
        _ = MultiLaneReceiveChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
}
