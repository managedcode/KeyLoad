using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnMaintenanceStage
{
    internal static AnnMaintenanceCapabilityResult Execute(DatabaseEngine database, AnnGenerationOwner owner,
        AnnStageStore stages, AnnMaintenanceSession session, PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, CancellationToken token)
    {
        var failures = new List<Exception>();
        AnnMaintenanceCapabilityResult? result = null;
        try
        {
            ServerFailureObserver.Observe(() => result = AnnSessionOperations.Execute(database, owner,
                stages, session, principal, request, configured, token), failures);
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
