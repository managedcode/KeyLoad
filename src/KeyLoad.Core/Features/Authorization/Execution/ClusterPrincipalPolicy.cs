using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Protects the persisted internal identity used for cluster membership operations.</summary>
public static class ClusterPrincipalPolicy
{
    private const int BootstrapPolicyEpoch = 1;
    private const int IsProtectedPrincipalGrantsEmptyCount = 0;
    private const int IsProtectedPrincipalFieldGrantsEmptyCount = 0;
    private const int IsProtectedPrincipalProjectsEmptyCount = 0;
    private const int ProtectedPolicyEpoch = 1;

    /// <summary>The stable principal identifier reserved for trusted cluster membership operations.</summary>
    public const string InternalPrincipalId = "keyload-internal-cluster";

    private const string SystemTenantId = "system";
    private const string ProtectedPrincipalError = "The internal cluster principal is protected from public configuration.";
    private const string MembershipOnlyError = "The internal cluster principal may only perform membership operations.";
    private const string MembershipPrincipalError = "Membership operations require the internal cluster principal.";
    private const string RecoveryRequiredError = "The protected cluster principal is missing or invalid at an existing apply cut.";

    /// <summary>Seeds the protected principal in the replicated catalog before the first applied operation.</summary>
    /// <param name="database">The database whose canonical store owns the principal catalog.</param>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    /// <exception cref="KeyLoadException">The protected record is missing or invalid after the apply cut has advanced.</exception>
    public static void Initialize(DatabaseEngine database)
    {
        const int EmptyPolicyEpoch = 0;

        ArgumentNullException.ThrowIfNull(database);

        database.Store.Commit((transaction, _) =>
        {
            var appliedCut = transaction.ReadOwnedValue(KeySpace.AppliedBytes) is { } appliedBytes
                ? NativeSerialization.Deserialize<long>(appliedBytes)
                : EmptyPolicyEpoch;

            PrincipalRecord? principal;
            try
            {
                principal = transaction.GetRecord<PrincipalRecord>(KeySpace.Principal(InternalPrincipalId));
            }
            catch (JsonException)
            {
                throw RecoveryRequired();
            }
            catch (KeyLoadException exception) when (exception.Code == ErrorCode.Corruption)
            {
                throw RecoveryRequired();
            }

            if (principal is null)
            {
                if (appliedCut != EmptyPolicyEpoch)
                {
                    throw RecoveryRequired();
                }

                transaction.PutRecord(KeySpace.Principal(InternalPrincipalId), CreateProtectedPrincipal());
                return true;
            }

            if (!IsProtectedPrincipal(principal))
            {
                throw RecoveryRequired();
            }

            return true;
        });
    }

    /// <summary>Rejects public configuration changes targeting the protected cluster principal.</summary>
    /// <param name="principalId">The principal identifier being configured.</param>
    /// <exception cref="ArgumentNullException"><paramref name="principalId"/> is null.</exception>
    /// <exception cref="KeyLoadException">The identifier is reserved for internal cluster membership.</exception>
    public static void RequireMutablePrincipal(string principalId)
    {
        ArgumentNullException.ThrowIfNull(principalId);
        RuntimeJournalIdentity.RequireMutablePrincipal(principalId);

        if (principalId == InternalPrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ProtectedPrincipalError);
        }
    }

    /// <summary>Rejects public API credentials targeting the protected cluster principal.</summary>
    /// <param name="principalId">The principal identifier receiving the credential.</param>
    /// <exception cref="ArgumentNullException"><paramref name="principalId"/> is null.</exception>
    /// <exception cref="KeyLoadException">The identifier is reserved for internal cluster membership.</exception>
    public static void RequirePublicCredential(string principalId)
    {
        ArgumentNullException.ThrowIfNull(principalId);
        RuntimeJournalIdentity.RequireMutablePrincipal(principalId);

        if (principalId == InternalPrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ProtectedPrincipalError);
        }
    }

    /// <summary>Restricts membership writes to the persisted internal principal and denies it every other operation.</summary>
    /// <param name="principal">The principal loaded from the persisted authorization catalog.</param>
    /// <param name="kind">The requested operation kind.</param>
    /// <exception cref="ArgumentNullException"><paramref name="principal"/> is null.</exception>
    /// <exception cref="KeyLoadException">The principal is not allowed to perform the requested operation.</exception>
    public static void RequireOperation(PrincipalRecord principal, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Id == InternalPrincipalId)
        {
            if (!IsProtectedPrincipal(principal))
            {
                throw RecoveryRequired();
            }

            if (kind != OperationKind.Membership)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, MembershipOnlyError);
            }

            return;
        }

        if (kind == OperationKind.Membership)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, MembershipPrincipalError);
        }
    }

    private static PrincipalRecord CreateProtectedPrincipal()
        => new(InternalPrincipalId, SystemTenantId, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = BootstrapPolicyEpoch
        };

    private static bool IsProtectedPrincipal(PrincipalRecord principal)
        => principal.Id == InternalPrincipalId
           && principal.TenantId == SystemTenantId
           && principal.ClusterAdministrator
           && principal.Grants is { Length: IsProtectedPrincipalGrantsEmptyCount }
           && principal.FieldGrants is { Length: IsProtectedPrincipalFieldGrantsEmptyCount }
           && principal.OwnerId is null
           && principal.Projects is { Length: IsProtectedPrincipalProjectsEmptyCount }
           && !principal.RestrictRows
           && !principal.Revoked
           && principal.ExpiresAt is null
           && principal.PolicyEpoch == ProtectedPolicyEpoch;

    private static KeyLoadException RecoveryRequired()
        => Errors.Fail(ErrorCode.RecoveryRequired, RecoveryRequiredError);
}
