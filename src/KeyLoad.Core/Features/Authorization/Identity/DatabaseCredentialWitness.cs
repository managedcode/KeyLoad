using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(AuthorizationNativeAliases.CredentialWitness)]
internal sealed record DatabaseCredentialWitness(
    [property: Orleans.Id(0)] string CredentialId,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> Digest);

public sealed partial class DatabaseEngine
{
    private const int NativeCredentialMinimumCharacters = 20;
    private const int NativeCredentialMaximumCharacters = 256;
    private const int NativeCredentialFirstIdentityCharacter = 0;
    private const char NativeCredentialIdentitySeparator = '.';
    private const string NativeCredentialRequired = "An API key is required.";

    internal static DatabaseCredentialWitness IssueCredentialWitness(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var separator = secret.IndexOf(NativeCredentialIdentitySeparator, StringComparison.Ordinal);
        if (secret.Length is < NativeCredentialMinimumCharacters or > NativeCredentialMaximumCharacters
            || separator <= NativeCredentialFirstIdentityCharacter)
        { throw Errors.Fail(ErrorCode.Unauthenticated, NativeCredentialRequired); }
        return new(secret[..separator], SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static PrincipalRecord RevalidateCredential(IKeyValueView view, DatabaseCredentialWitness witness,
        string? expectedPrincipal, DateTimeOffset now) =>
        DatabaseCredentialValidation.Require(view, witness, expectedPrincipal, now);
}
