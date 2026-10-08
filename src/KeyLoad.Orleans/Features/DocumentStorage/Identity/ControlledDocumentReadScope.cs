namespace KeyLoad.Orleans;

internal static class ControlledDocumentReadScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var controlled = request.ReadKind == GrainReadKind.ControlledDocument;
        if (request.Purpose != ControlledDocumentReadProtocol.Purpose)
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
