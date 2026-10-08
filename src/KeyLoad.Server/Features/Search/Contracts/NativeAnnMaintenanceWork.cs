using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal readonly record struct NativeAnnMaintenanceWork(Guid SessionId, long WorkUnits,
    AnnMaintenanceCapabilityKind? Stage = null, long WrittenBytes = NativeAnnMaintenanceWork.NoWrittenBytes)
{
    internal const long NoWrittenBytes = 0;
}
