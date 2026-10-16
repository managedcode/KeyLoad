namespace KeyLoad.Clustering;

internal static class ResourceRetirementReadScope
{
    internal const string Purpose = "keyload.resource-retirement.read.v1";
    internal const string InvalidRequest = "The original bounded resource retirement query is invalid.";

    internal static bool Handles(GrainReadKind kind) => kind == GrainReadKind.ResourceRetirement;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var retirement = request.ReadKind == GrainReadKind.ResourceRetirement;
        if (request.Purpose != Purpose && !retirement)
        {
            return false;
        }
        if (!retirement || request.Purpose != Purpose || request.CommandKind is not null
            || request.CommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
