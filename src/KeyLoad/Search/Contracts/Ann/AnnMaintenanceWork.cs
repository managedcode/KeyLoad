using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.Search;

internal readonly record struct AnnMaintenanceWork(Guid SessionId, long WorkUnits,
    AnnMaintenanceCapabilityKind? Stage = null, long WrittenBytes = AnnMaintenanceWork.NoWrittenBytes)
{
    internal const long NoWrittenBytes = 0;
}
