using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizationExecution operations against the caller's database context.</summary>
public static class AuthorizationExecution
{
    internal const string UnavailableCredentialDetail = DatabaseCredentialValidation.UnavailableCredentialDetail;
    /// <summary>Creates the initial administrator and credential if that principal is absent.</summary>
    /// <param name = "administrator">Initial persisted cluster administrator.</param>
    /// <param name = "apiKey">Credential belonging to that administrator.</param>

    /// <param name = "engine">The shared database execution context.</param>
    public static void Bootstrap(this DatabaseEngine engine, PrincipalRecord administrator, ApiKeyRecord apiKey)
    {
        ArgumentNullException.ThrowIfNull(engine);
        const string BootstrapDetailText = "Bootstrap requires a cluster administrator.";
        ArgumentNullException.ThrowIfNull(administrator);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!administrator.ClusterAdministrator || administrator.Id != apiKey.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.Validation, BootstrapDetailText);
        }

        engine.Store.Commit((tx, _) =>
        {
            if (tx.ReadOwnedValue(KeySpace.Principal(administrator.Id)) is null)
            {
                tx.PutRecord(KeySpace.Principal(administrator.Id), administrator);
                tx.PutRecord(KeySpace.ApiKey(apiKey.Id), apiKey);
            }

            return true;
        });
    }

    /// <summary>Reads an active persisted principal at the operation's evaluated time.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "id">Principal identifier.</param>
    /// <param name = "now">Business time used to evaluate expiry.</param>
    /// <returns>The active persisted principal.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static PrincipalRecord Principal(this DatabaseEngine engine, IKeyValueView view, string id, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return DatabaseCredentialValidation.ReadPrincipal(view, id, now);
    }

    /// <summary>Verifies an API credential and its active persisted principal.</summary>
    /// <param name = "secret">Complete API key secret.</param>
    /// <param name = "now">Business time used to evaluate expiry.</param>
    /// <returns>The verified persisted principal identifier.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static string Authenticate(this DatabaseEngine engine, string secret, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var witness = AuthorizationIdentity.IssueCredentialWitness(secret);
        return engine.Store.Read(view => AuthorizationIdentity.RevalidateCredential(view, witness, null, now).Id);
    }

    /// <summary>Verifies the same persisted credential on the caller's original bounded read frame.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "secret">The secret.</param>
    /// <param name = "now">The now.</param>
    /// <param name = "work">The work.</param>
    internal static PrincipalRecord Authenticate(this DatabaseEngine engine, string secret, DateTimeOffset now, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var witness = AuthorizationIdentity.IssueCredentialWitness(secret);
        var principal = engine.Store.Read(raw => AuthorizationIdentity.RevalidateCredential(work.CreateView(raw), witness, null, now));
        work.Check();
        return principal;
    }

    /// <summary>Creates a credential record containing only the secret's SHA-256 verifier.</summary>
    /// <param name = "id">Credential identifier.</param>
    /// <param name = "principal">Owning principal identifier.</param>
    /// <param name = "secret">Secret to hash, which is not retained in the record.</param>
    /// <param name = "expiresAt">Optional credential expiry.</param>
    /// <returns>The persisted verifier record.</returns>
    public static ApiKeyRecord Credential(string id, string principal, string secret, DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new(id, principal, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), expiresAt);
    }
}
