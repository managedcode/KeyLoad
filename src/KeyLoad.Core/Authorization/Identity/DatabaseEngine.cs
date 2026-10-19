using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int CredentialMinimumCharacters = 20;
    private const int CredentialMaximumCharacters = 256;
    private const int CredentialFirstIdentityCharacter = 0;
    private const char CredentialIdentitySeparator = '.';
    private const string CredentialRequired = "An API key is required.";

    internal static DatabaseCredentialWitness IssueCredentialWitness(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var separator = secret.IndexOf(CredentialIdentitySeparator, StringComparison.Ordinal);
        if (secret.Length is < CredentialMinimumCharacters or > CredentialMaximumCharacters
            || separator <= CredentialFirstIdentityCharacter)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, CredentialRequired);
        }
        return new(secret[..separator], SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static PrincipalRecord RevalidateCredential(IKeyValueView view, DatabaseCredentialWitness witness,
        string? expectedPrincipal, DateTimeOffset now) =>
        DatabaseCredentialValidation.Require(view, witness, expectedPrincipal, now);
}
