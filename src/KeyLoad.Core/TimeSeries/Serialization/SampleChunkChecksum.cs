using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkChecksum
{
    private const string DomainText = "keyload.sample-chunk.v1";
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(DomainText);

    internal static byte[] Compute(SampleChunkPayload payload, SampleChunkWork budget, int hashChunkBytes)
    {
        const int ChecksumHeaderFields = 2;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Domain, budget, hashChunkBytes);
        Span<byte> header = stackalloc byte[sizeof(int) * ChecksumHeaderFields];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[sizeof(int)..], payload.RecordCount);
        Append(hash, header, budget, hashChunkBytes);
        AppendColumn(hash, payload.UtcTicks.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.Offsets.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.Sequences.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.Values.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.Series.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.EventIds.Span, budget, hashChunkBytes);
        AppendColumn(hash, payload.Tags.Span, budget, hashChunkBytes);
        budget.Check();
        return hash.GetHashAndReset();
    }

    internal static void Verify(SampleChunkPayload payload, SampleChunkWork budget, int hashChunkBytes)
    {
        SampleChunkWire.Require(payload.Checksum.Length == SHA256.HashSizeInBytes);
        var actual = Compute(payload, budget, hashChunkBytes);
        SampleChunkWire.Require(CryptographicOperations.FixedTimeEquals(actual, payload.Checksum.Span));
    }

    private static void AppendColumn(IncrementalHash hash, ReadOnlySpan<byte> column, SampleChunkWork budget, int hashChunkBytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, column.Length);
        Append(hash, length, budget, hashChunkBytes);
        Append(hash, column, budget, hashChunkBytes);
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value, SampleChunkWork budget, int hashChunkBytes)
    {
        const int OffsetInitialValue = 0;

        for (var offset = OffsetInitialValue; offset < value.Length; offset += hashChunkBytes)
        {
            budget.Check();
            hash.AppendData(value.Slice(offset, Math.Min(hashChunkBytes, value.Length - offset)));
        }
        budget.Check();
    }
}
