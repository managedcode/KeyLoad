using System.Security.Cryptography;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkEncodedDigest
{
    internal static void Require(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> expected,
        SampleChunkWork work, int hashChunkBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var offset = SampleChunkLifecycleProtocol.FirstIndex; offset < bytes.Length; offset += hashChunkBytes)
        {
            work.Check();
            hash.AppendData(bytes.Slice(offset, Math.Min(hashChunkBytes, bytes.Length - offset)));
        }
        work.Check();
        if (!hash.GetHashAndReset().AsSpan().SequenceEqual(expected))
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
    }
}
