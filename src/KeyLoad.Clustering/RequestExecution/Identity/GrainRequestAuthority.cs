using KeyLoad.Core;
using KeyLoad.Core.RuntimeJournaling.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Clustering;

internal static class GrainRequestAuthority
{
    internal static PrincipalRecord Reload(DatabaseEngine database, string principalId, TimeProvider clock)
    {
        try
        {
            RuntimeJournalIdentity.RequirePublicCredential(principalId);
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
        if (envelope.Purpose != GrainContracts.RuntimeJournalPurpose
            || envelope.PrincipalId != RuntimeJournalIdentity.ProtectedPrincipalId)
        {
            return Reload(database, envelope.PrincipalId!, clock);
        }
        var principal = database.Store.Read(view => database.Principal(view, envelope.PrincipalId, clock.GetUtcNow()));
        RuntimeJournalIdentity.RequireProtected(principal);
        return principal;
    }

    internal static PrincipalRecord ReloadForRequest(DatabaseEngine database, IKeyValueView view,
        GrainRequestEnvelope envelope)
    {
        try
        {
            var protectedJournal = envelope.Purpose == GrainContracts.RuntimeJournalPurpose
                && envelope.PrincipalId == RuntimeJournalIdentity.ProtectedPrincipalId;
            if (!protectedJournal)
            {
                RuntimeJournalIdentity.RequirePublicCredential(envelope.PrincipalId!);
            }
            var principal = database.Principal(view, envelope.PrincipalId!, database.EvaluationClock.GetUtcNow());
            if (protectedJournal)
            {
                RuntimeJournalIdentity.RequireProtected(principal);
            }
            return principal;
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static PrincipalRecord ReloadDocumentPrincipal(DatabaseEngine database, IKeyValueView view, string principalId)
    {
        try
        {
            RuntimeJournalIdentity.RequirePublicCredential(principalId);
            return database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static PrincipalRecord Reload(DatabaseEngine database, string principalId, TimeProvider clock,
        ReadExecutionBudget work)
    {
        try
        {
            work.Check();
            RuntimeJournalIdentity.RequirePublicCredential(principalId);
            var principal = database.Store.Read(raw => database.Principal(work.CreateView(raw), principalId, clock.GetUtcNow()));
            work.Check();
            return principal;
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.Authorization);
            throw;
        }
    }

    internal static PrincipalRecord ReloadForRequest(DatabaseEngine database, GrainRequestEnvelope envelope,
        TimeProvider clock, ReadExecutionBudget work)
    {
        if (envelope.Purpose != GrainContracts.RuntimeJournalPurpose
            || envelope.PrincipalId != RuntimeJournalIdentity.ProtectedPrincipalId)
        {
            return Reload(database, envelope.PrincipalId!, clock, work);
        }
        work.Check();
        var principal = database.Store.Read(raw => database.Principal(work.CreateView(raw),
            envelope.PrincipalId, clock.GetUtcNow()));
        RuntimeJournalIdentity.RequireProtected(principal);
        work.Check();
        return principal;
    }

    internal static CredentialAuthentication Authenticate(DatabaseEngine database, string secret, TimeProvider clock)
    {
        try
        {
            var key = database.AuthenticateKey(secret, clock.GetUtcNow());
            return new(Reload(database, key.PrincipalId, clock), key.ExpiresAt);
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
