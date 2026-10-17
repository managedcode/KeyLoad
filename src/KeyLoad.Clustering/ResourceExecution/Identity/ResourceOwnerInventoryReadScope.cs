namespace KeyLoad.Clustering;

internal static class ResourceOwnerInventoryReadScope
{
    internal const string Purpose = "keyload.resource-owner-inventory.read.v1";
    internal const string InvalidRequest = "The original bounded resource owner inventory query is invalid.";

    internal static bool Handles(GrainReadKind kind) => kind == GrainReadKind.ResourceOwnerInventory;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var inventory = request.ReadKind == GrainReadKind.ResourceOwnerInventory;
        if (request.Purpose != Purpose && !inventory)
        {
            return false;
        }
        if (!inventory || request.Purpose != Purpose || request.CommandKind is not null
            || request.CommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
