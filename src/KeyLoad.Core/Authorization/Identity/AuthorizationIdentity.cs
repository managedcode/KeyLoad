using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizationIdentity operations against the caller's database context.</summary>
public static class AuthorizationIdentity
{
    internal const int CredentialMinimumCharacters = 20;
    internal const int CredentialMaximumCharacters = 256;
    internal const int CredentialFirstIdentityCharacter = 0;
    internal const char CredentialIdentitySeparator = '.';
    internal const string CredentialRequired = "An API key is required.";
    internal static DatabaseCredentialWitness IssueCredentialWitness(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var separator = secret.IndexOf(CredentialIdentitySeparator, StringComparison.Ordinal);
        if (secret.Length is < CredentialMinimumCharacters or > CredentialMaximumCharacters || separator <= CredentialFirstIdentityCharacter)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, CredentialRequired);
        }

        return new(secret[..separator], SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    internal static PrincipalRecord RevalidateCredential(IKeyValueView view, DatabaseCredentialWitness witness, string? expectedPrincipal, DateTimeOffset now) => DatabaseCredentialValidation.Require(view, witness, expectedPrincipal, now);
}
