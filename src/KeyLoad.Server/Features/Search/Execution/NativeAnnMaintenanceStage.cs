using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnMaintenanceStage
{
    internal static AnnMaintenanceCapabilityResult Execute(DatabaseEngine database, NativeAnnGenerationOwner owner,
        NativeAnnStageStore stages, NativeAnnMaintenanceSession session, PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, TimeProvider clock, CancellationToken token)
    {
        var failures = new List<Exception>();
        AnnMaintenanceCapabilityResult? result = null;
        try
        {
            ServerFailureObserver.Observe(() => result = NativeAnnSessionOperations.Execute(database, owner,
                stages, session, principal, request, configured, clock, token), failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => owner.MaintenanceMemory.Retain(session.Memory,
                session.RetainedBytes), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}
