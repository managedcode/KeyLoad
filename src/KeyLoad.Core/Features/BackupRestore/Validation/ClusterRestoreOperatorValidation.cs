using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Verifies a separately configured restore operator against the selected native persisted cut.</summary>
public static class ClusterRestoreOperatorValidation
{
    private const string AdministratorRequired = "Restore requires an active persisted cluster administrator.";

    /// <summary>Reads and verifies the credential and its current administrator record in the supplied gated view.</summary>
    /// <param name="view">Actual verified archive or restored canonical store view owned by the operator.</param>
    /// <param name="secret">Separately configured API credential, never a snapshot-supplied trusted role.</param>
    /// <param name="now">Actual operator evaluation time for credential and principal expiry.</param>
    /// <returns>The native persisted administrator record at this selected cut.</returns>
    public static PrincipalRecord RequireAdministrator(IKeyValueView view, string secret, DateTimeOffset now)
    {
        var witness = DatabaseEngine.IssueCredentialWitness(secret);
        var principal = DatabaseCredentialValidation.Require(view, witness, null, now);
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, AdministratorRequired); }
        return principal;
    }
}
