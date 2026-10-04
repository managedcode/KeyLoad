using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Creates the initial administrator and credential if that principal is absent.</summary>
    /// <param name="administrator">Initial persisted cluster administrator.</param>
    /// <param name="apiKey">Credential belonging to that administrator.</param>
    public void Bootstrap(PrincipalRecord administrator, ApiKeyRecord apiKey)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!administrator.ClusterAdministrator || administrator.Id != apiKey.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.Validation, "Bootstrap requires a cluster administrator.");
        }

        Store.Commit((tx, _) =>
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
    /// <param name="view">Current gated storage view.</param>
    /// <param name="id">Principal identifier.</param>
    /// <param name="now">Business time used to evaluate expiry.</param>
    /// <returns>The active persisted principal.</returns>
    public PrincipalRecord Principal(IKeyValueView view, string id, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);
        var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(id));
        if (principal is null || principal.Revoked || principal.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        }

        return principal;
    }
    /// <summary>Verifies an API credential and its active persisted principal.</summary>
    /// <param name="secret">Complete API key secret.</param>
    /// <param name="now">Business time used to evaluate expiry.</param>
    /// <returns>The verified persisted principal identifier.</returns>
    public string Authenticate(string secret, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var separator = secret.IndexOf('.', StringComparison.Ordinal);
        if (secret.Length is < 20 or > 256 || separator <= 0)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, "An API key is required.");
        }

        return Store.Read(view =>
        {
            var key = view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(secret[..separator]));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            byte[] verifier;
            try
            { verifier = key is null ? new byte[32] : Convert.FromHexString(key.Verifier); }
            catch (FormatException) { throw Errors.Fail(ErrorCode.Corruption, "A credential verifier is invalid."); }
            if (key is null || !CryptographicOperations.FixedTimeEquals(hash, verifier) || key.Revoked || key.ExpiresAt <= now)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
            }

            return Principal(view, key.PrincipalId, now).Id;
        });
    }
    /// <summary>Creates a credential record containing only the secret's SHA-256 verifier.</summary>
    /// <param name="id">Credential identifier.</param>
    /// <param name="principal">Owning principal identifier.</param>
    /// <param name="secret">Secret to hash, which is not retained in the record.</param>
    /// <param name="expiresAt">Optional credential expiry.</param>
    /// <returns>The persisted verifier record.</returns>
    public static ApiKeyRecord Credential(string id, string principal, string secret, DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new(id, principal, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), expiresAt);
    }

}
