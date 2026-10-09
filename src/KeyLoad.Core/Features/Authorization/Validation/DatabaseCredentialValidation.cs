using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class DatabaseCredentialValidation
{
    internal const string UnavailableCredentialDetail = "The credential is unavailable or expired.";
    private const string NativeCredentialVerifierInvalid = "A credential verifier is invalid.";
    private const int NativeCredentialDigestBytes = 32;

    internal static PrincipalRecord Require(IKeyValueView view, DatabaseCredentialWitness witness,
        string? expectedPrincipal, DateTimeOffset now)
    {
        var key = view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(witness.CredentialId));
        byte[] verifier;
        try
        { verifier = key is null ? new byte[NativeCredentialDigestBytes] : Convert.FromHexString(key.Verifier); }
        catch (FormatException)
        { throw Errors.Fail(ErrorCode.Corruption, NativeCredentialVerifierInvalid); }
        if (witness.Digest.Length != NativeCredentialDigestBytes || key is null
            || !CryptographicOperations.FixedTimeEquals(witness.Digest.Span, verifier)
            || key.Revoked || key.ExpiresAt <= now || expectedPrincipal is not null && key.PrincipalId != expectedPrincipal)
        { throw Errors.Fail(ErrorCode.Unauthenticated, UnavailableCredentialDetail); }
        return ReadPrincipal(view, key.PrincipalId, now);
    }

    internal static PrincipalRecord ReadPrincipal(IKeyValueView view, string id, DateTimeOffset now)
    {
        const string PrincipalDetailText = UnavailableCredentialDetail;

        ArgumentNullException.ThrowIfNull(view);
        var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(id));
        if (principal is null || principal.Revoked || principal.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PrincipalDetailText);
        }

        return principal;
    }
}
