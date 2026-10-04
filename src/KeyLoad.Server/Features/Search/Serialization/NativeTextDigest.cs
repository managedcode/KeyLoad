using System.Buffers;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextDigest
{
    internal static byte[] HashBounded(Stream input, long expectedLength, long remaining,
        ReadExecutionBudget? budget)
    {
        budget?.Check();
        if (expectedLength < 0 || remaining < 0 || expectedLength > remaining)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(NativeTextProtocol.HashBufferBytes);
        try
        {
            return HashStream(input, expectedLength, remaining, budget, hash, buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static byte[] HashStream(Stream input, long expectedLength, long remaining,
        ReadExecutionBudget? budget, IncrementalHash hash, byte[] buffer)
    {
        long total = 0;
        while (true)
        {
            budget?.Check();
            var read = input.Read(buffer, 0, NativeTextProtocol.HashBufferBytes);
            if (read == 0)
            {
                break;
            }
            if (read > expectedLength - total || read > remaining - total)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            hash.AppendData(buffer, 0, read);
            total += read;
        }
        budget?.Check();
        if (total != expectedLength)
        {
            throw NativeTextErrors.Corrupt();
        }
        return hash.GetHashAndReset();
    }
}
