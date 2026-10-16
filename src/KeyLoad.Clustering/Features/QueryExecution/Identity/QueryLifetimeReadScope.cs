namespace KeyLoad.Clustering;

internal static class QueryLifetimeReadScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var selected = request.ReadKind is GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2;
        if (request.Purpose != QueryLifetimeReadProtocol.Purpose)
        {
            if (selected)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return false;
        }
        if (!selected || request.CommandKind is not null || request.CommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
