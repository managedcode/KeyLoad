using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionObservationSource
{
    internal static void RequireSame(PartitionHost partition, RemoteAnnReadFenceV2 captured,
        AnnMaintenanceRequest request, ReadExecutionBudget work)
    {
        work.Check();
        partition.Database.ValidateRemoteAnnLifetimeRead(captured, request.Consumer, request.Collection, request.Field,
            request.Space, request.IndexGeneration, work);
    }
}
