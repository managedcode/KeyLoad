using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class TextProjectionLifecycle
{
    internal static TextProjectionScope CreateScope(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request)
    {
        var identity = database.Store.Identity;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, request.Partition, request.Collection, request.TextField!, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion);
    }

    internal static void Dispose(ITextProjectionLease? lease, Exception? primaryFailure)
    {
        if (lease is null)
        {
            return;
        }
        try
        {
            lease.Dispose();
        }
        catch (Exception cleanupFailure) when (primaryFailure is not null)
        {
            throw new AggregateException(primaryFailure, cleanupFailure);
        }
    }
}
