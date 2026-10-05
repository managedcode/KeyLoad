using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Orleans;

internal static class GrainRequestAuthority
{
    internal static PrincipalRecord Reload(DatabaseEngine database, string principalId, TimeProvider clock)
    {
        try
        {
            ClusterPrincipalPolicy.RequirePublicCredential(principalId);
            return database.Store.Read(view => database.Principal(view, principalId, clock.GetUtcNow()));
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static PrincipalRecord ReloadForRequest(DatabaseEngine database, GrainRequestEnvelope envelope, TimeProvider clock)
    {
        if (envelope.Purpose != GrainNativeContracts.RuntimeJournalPurpose
            || envelope.PrincipalId != RuntimeJournalIdentity.ProtectedPrincipalId)
        {
            return Reload(database, envelope.PrincipalId!, clock);
        }
        var principal = database.Store.Read(view => database.Principal(view, envelope.PrincipalId, clock.GetUtcNow()));
        RuntimeJournalIdentity.RequireProtected(principal);
        return principal;
    }

    internal static PrincipalRecord Authenticate(DatabaseEngine database, ReadOnlyMemory<byte> payload, TimeProvider clock)
    {
        try
        {
            var secret = GrainNativePayload.Read<string>(payload);
            var principalId = database.Authenticate(secret, clock.GetUtcNow());
            return Reload(database, principalId, clock);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static void RequireAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        {
            var error = Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw error;
        }
    }
}
