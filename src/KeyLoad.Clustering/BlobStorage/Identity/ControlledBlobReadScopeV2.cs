namespace KeyLoad.Clustering;

internal static class ControlledBlobReadScopeV2
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var controlled = request.ReadKind == GrainReadKind.ControlledBlobV2;
        if (request.Purpose != ControlledBlobLifetimeReadProtocol.Purpose)
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
