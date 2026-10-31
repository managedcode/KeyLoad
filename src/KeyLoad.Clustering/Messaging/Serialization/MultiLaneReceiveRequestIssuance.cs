namespace KeyLoad.Clustering;

/// <summary>Issues the original bounded multi-lane receive child without changing its execution payload.</summary>
internal static class MultiLaneReceiveRequestIssuance
{
    internal static GrainRequestEnvelope CreateMultiLaneReceiveChild(GrainRequestCodec codec, Guid requestId, string principalId, Guid commandId,
        object payload)
    {
        var envelope = new GrainRequestEnvelope
        {
            Purpose = MultiLaneReceiveChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = codec.Incarnation,
            CommandKind = OperationKind.Receive,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload)
        };
        _ = MultiLaneReceiveChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
}
