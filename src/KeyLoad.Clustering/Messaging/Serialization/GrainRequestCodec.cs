namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateMultiLaneReceiveChild(Guid requestId, string principalId, Guid commandId,
        object payload, DateTimeOffset expiry)
        => MultiLaneReceiveRequestIssuance.CreateMultiLaneReceiveChild(this, requestId, principalId, commandId, payload, expiry);
}
