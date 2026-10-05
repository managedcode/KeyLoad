namespace KeyLoad.Core.Features.ClusterRouting.Identity;

/// <summary>Enforces the private identity boundary for runtime journal requests.</summary>
public static class RuntimeJournalIdentity
{
    /// <summary>Identifies the sole private principal allowed to access journal state.</summary>
    public const string ProtectedPrincipalId = Contracts.RuntimeJournalProtocol.IdentityId;

    /// <summary>Requires the exact persisted journal-only principal contract.</summary>
    public static void RequireProtected(PrincipalRecord principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Id != ProtectedPrincipalId || principal.TenantId != Contracts.RuntimeJournalProtocol.SystemTenant
            || !principal.Grants.IsDefaultOrEmpty || !principal.FieldGrants.IsDefaultOrEmpty || !principal.Projects.IsDefaultOrEmpty
            || principal.ClusterAdministrator || principal.PolicyEpoch != Contracts.RuntimeJournalProtocol.ProtectedPolicyEpoch || principal.Revoked
            || principal.ExpiresAt is not null || principal.OwnerId is not null || principal.RestrictRows)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Contracts.RuntimeJournalProtocol.InvalidState);
        }
    }

    /// <summary>Rejects mutation of the private system identity by public principal operations.</summary>
    public static void RequireMutablePrincipal(string principalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        if (principalId == ProtectedPrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Contracts.RuntimeJournalProtocol.InvalidRequest);
        }
    }

    /// <summary>Rejects public credentials which target the protected system identity.</summary>
    public static void RequirePublicCredential(string principalId)
    {
        RequireMutablePrincipal(principalId);
    }

    /// <summary>Checks the closed operation boundary for the protected principal.</summary>
    public static void RequireOperation(PrincipalRecord principal, OperationKind kind,
        RuntimeJournalMutation? mutation = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var protectedPrincipal = principal.Id == ProtectedPrincipalId;
        if (protectedPrincipal)
        {
            RequireProtected(principal);
            if (kind != OperationKind.RuntimeJournal || mutation?.Action == RuntimeJournalAction.BootstrapIdentity)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Contracts.RuntimeJournalProtocol.InvalidRequest);
            }
            return;
        }

        if (kind == OperationKind.RuntimeJournal && (mutation?.Action != RuntimeJournalAction.BootstrapIdentity
            || !principal.ClusterAdministrator))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Contracts.RuntimeJournalProtocol.InvalidRequest);
        }
    }
}
