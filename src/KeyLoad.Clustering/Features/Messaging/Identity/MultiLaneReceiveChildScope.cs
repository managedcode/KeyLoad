namespace KeyLoad.Clustering;

internal static class MultiLaneReceiveChildScope
{
    internal const string Purpose = "keyload-multilane-receive-child-v1";

    internal static bool IsSelected(GrainRequestEnvelope request)
        => request.Purpose == Purpose && request.CommandKind == OperationKind.Receive
            && request.ReadKind is null && request.CommandId != Guid.Empty;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != Purpose)
        {
            return false;
        }
        if (!IsSelected(request))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
