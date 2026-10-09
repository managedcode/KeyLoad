namespace KeyLoad.Orleans;

internal static class ControlledBlobReadScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var controlled = request.ReadKind == GrainReadKind.ControlledBlob;
        if (request.Purpose != ControlledBlobReadProtocol.Purpose)
        {
            if (controlled)
            { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
            return false;
        }
        if (!controlled || request.CommandKind is not null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
