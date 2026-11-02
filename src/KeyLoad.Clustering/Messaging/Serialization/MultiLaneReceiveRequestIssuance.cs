namespace KeyLoad.Clustering;

/// <summary>Issues the original bounded multi-lane receive child without changing its execution payload.</summary>
internal static class MultiLaneReceiveRequestIssuance
{
    internal static GrainRequestEnvelope CreateMultiLaneReceiveChild(GrainRequestCodec codec, Guid requestId, string principalId, Guid commandId,
        ReceiveRequest payload)
    {
        var envelope = new GrainRequestEnvelope
        {
            Purpose = MultiLaneReceiveChildScope.Purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = codec.Incarnation,
            CommandKind = OperationKind.Receive,
            CommandId = commandId,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = GrainPayload.Copy(payload)
        };
        _ = MultiLaneReceiveChildScope.Validate(envelope);
        return codec.Issue(envelope);
    }
}
