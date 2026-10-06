using Microsoft.Extensions.Options;
using System.Buffers;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextDigest
{
    internal static byte[] HashBounded(Stream input, long expectedLength, long remaining, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int ExpectedLengthValidationBoundary = 0;
        const int RemainingValidationBoundary = 0;

        budget?.Check();
        if (expectedLength < ExpectedLengthValidationBoundary || remaining < RemainingValidationBoundary || expectedLength > remaining)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(executionOptions.Value.HashBufferBytes);
        try
        {
            return HashStream(input, expectedLength, remaining, budget, hash, buffer, executionOptions: executionOptions);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static byte[] HashStream(Stream input, long expectedLength, long remaining, ReadExecutionBudget? budget, IncrementalHash hash, byte[] buffer, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int TotalInitialValue = 0;
        const int OffsetEmptyCount = 0;
        const int EmptyRead = 0;

        long total = TotalInitialValue;
        while (true)
        {
            budget?.Check();
            var read = input.Read(buffer, OffsetEmptyCount, executionOptions.Value.HashBufferBytes);
            if (read == EmptyRead)
            {
                break;
            }
            if (read > expectedLength - total || read > remaining - total)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            hash.AppendData(buffer, OffsetEmptyCount, read);
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
