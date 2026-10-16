namespace KeyLoad.Clustering;

internal static class ControlledBlobOutcomeReadScopeV2
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var controlled = request.ReadKind == GrainReadKind.ControlledBlobOutcomeV2;
        if (request.Purpose != ControlledBlobOutcomeReadProtocolV2.Purpose)
        {
            if (controlled)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return false;
        }
        if (!controlled || request.CommandKind is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
