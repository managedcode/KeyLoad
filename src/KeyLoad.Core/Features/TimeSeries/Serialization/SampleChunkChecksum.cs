using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkChecksum
{
    private const string DomainText = "keyload.sample-chunk.v1";
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(DomainText);

    internal static byte[] Compute(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Domain, budget);
        Span<byte> header = stackalloc byte[sizeof(int) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[sizeof(int)..], payload.RecordCount);
        Append(hash, header, budget);
        AppendColumn(hash, payload.UtcTicks.Span, budget);
        AppendColumn(hash, payload.Offsets.Span, budget);
        AppendColumn(hash, payload.Sequences.Span, budget);
        AppendColumn(hash, payload.Values.Span, budget);
        AppendColumn(hash, payload.Series.Span, budget);
        AppendColumn(hash, payload.EventIds.Span, budget);
        AppendColumn(hash, payload.Tags.Span, budget);
        budget.Check();
        return hash.GetHashAndReset();
    }

    internal static void Verify(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        SampleChunkWire.Require(payload.Checksum.Length == SHA256.HashSizeInBytes);
        var actual = Compute(payload, budget);
        SampleChunkWire.Require(CryptographicOperations.FixedTimeEquals(actual, payload.Checksum.Span));
    }

    private static void AppendColumn(IncrementalHash hash, ReadOnlySpan<byte> column, ReadExecutionBudget budget)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, column.Length);
        Append(hash, length, budget);
        Append(hash, column, budget);
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value, ReadExecutionBudget budget)
    {
        for (var offset = 0; offset < value.Length; offset += SampleChunkWire.HashChunkBytes)
        {
            budget.Check();
            hash.AppendData(value.Slice(offset, Math.Min(SampleChunkWire.HashChunkBytes, value.Length - offset)));
        }
        budget.Check();
    }
}
