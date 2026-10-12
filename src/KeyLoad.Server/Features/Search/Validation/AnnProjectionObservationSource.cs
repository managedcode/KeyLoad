using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionObservationSource
{
    internal static void RequireSame(PartitionHost partition, RemoteAnnReadFence captured,
        AnnMaintenanceRequest request, ReadExecutionBudget work)
    {
        work.Check();
        partition.Database.ValidateRemoteAnnRead(captured, request.Consumer, request.Collection, request.Field,
            request.Space, request.IndexGeneration, work);
    }
}
