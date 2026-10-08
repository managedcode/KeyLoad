using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoveragePackageDigestValidation
{
    private const int EncodedSha512Length = 88;

    internal static bool IsCanonicalSha512(string value)
    {
        if (value.Length != EncodedSha512Length)
        {
            return false;
        }
        Span<byte> bytes = stackalloc byte[SHA512.HashSizeInBytes];
        return Convert.TryFromBase64Chars(value.AsSpan(), bytes, out var written)
            && written == SHA512.HashSizeInBytes
            && Convert.ToBase64String(bytes) == value;
    }
}
