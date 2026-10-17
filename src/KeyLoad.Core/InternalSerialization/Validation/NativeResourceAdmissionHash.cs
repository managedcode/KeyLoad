using System.Security.Cryptography;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool MatchesResourceAdmissionHash(ReadOnlySpan<byte> admission, ReadOnlySpan<byte> expected)
    {
        if (admission.IsEmpty)
        {
            return expected.IsEmpty;
        }
        Span<byte> actual = stackalloc byte[KeyLoad.Core.Features.InternalSerialization.NativeAuthorityContract.DigestBytes];
        _ = SHA256.HashData(admission, actual);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
