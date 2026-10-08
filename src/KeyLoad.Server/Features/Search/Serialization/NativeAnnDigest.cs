using System.Security.Cryptography;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnDigest
{
    internal const int Bytes = 32;

    internal static void Require(byte[] expected, byte[] actual)
    {
        if (expected is null || expected.Length != Bytes || actual is null || actual.Length != Bytes
            || !CryptographicOperations.FixedTimeEquals(expected, actual))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }
}
