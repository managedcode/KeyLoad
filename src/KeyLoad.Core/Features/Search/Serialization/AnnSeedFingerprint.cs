using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedFingerprint
{
    private const string Domain = "keyload.ann.seed.corpus.v1";
    private const string TextTooLong = "A bounded ANN seed text field exceeded its admitted scratch.";
    private const string DigestFailure = "The ANN seed digest could not be finalized.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Compute(AnnSeedScope scope, ImmutableArray<VectorRecord> records,
        AnnSeedWork work, byte[] scratch)
    {
        const int RowInitialValue = 0;
        const int Sha256DigestBytes = 32;
        const int HexDigestCharacters = 64;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, scratch, Domain, work);
        AppendText(hash, scratch, scope.Field, work);
        AppendText(hash, scratch, scope.Space.Id, work);
        AppendInt32(hash, scope.Space.Dimension, work);
        AppendInt32(hash, (int)scope.Space.Metric, work);
        AppendText(hash, scratch, scope.Space.Model, work);
        AppendText(hash, scratch, scope.Space.Version, work);
        AppendInt32(hash, records.Length, work);
        for (var row = RowInitialValue; row < records.Length; row++)
        {
            work.Check();
            var record = records[row];
            AppendText(hash, scratch, record.DocumentId, work);
            AppendInt64(hash, record.DocumentRevision, work);
            AppendFloatBlocks(hash, scratch, record.Values, work);
        }
        work.Check();
        Span<byte> digest = stackalloc byte[Sha256DigestBytes];
        if (!hash.TryGetHashAndReset(digest, out var written) || written != digest.Length)
        {
            throw new InvalidOperationException(DigestFailure);
        }
        work.Charge(HexDigestCharacters);
        return Convert.ToHexStringLower(digest);
    }

    private static void AppendText(IncrementalHash hash, byte[] scratch, string value, AnnSeedWork work)
    {
        const int StartEmptyCount = 0;
        const int OffsetEmptyCount = 0;

        var length = StrictUtf8.GetByteCount(value);
        AppendInt32(hash, length, work);
        work.Check();
        if (length > scratch.Length)
        {
            throw new InvalidOperationException(TextTooLong);
        }
        var written = StrictUtf8.GetBytes(value.AsSpan(), scratch.AsSpan(StartEmptyCount, length));
        work.Charge(written);
        hash.AppendData(scratch, OffsetEmptyCount, written);
    }

    private static void AppendFloatBlocks(IncrementalHash hash, byte[] scratch,
        ImmutableArray<float> values, AnnSeedWork work)
    {
        const int FirstInitialValue = 0;
        const int OffsetInitialValue = 0;
        const int OffsetEmptyCount = 0;

        var first = FirstInitialValue;
        while (first < values.Length)
        {
            work.Check();
            var count = Math.Min(scratch.Length / sizeof(int), values.Length - first);
            for (var offset = OffsetInitialValue; offset < count; offset++)
            {
                work.Charge();
                var bits = BitConverter.SingleToInt32Bits(values[first + offset]);
                BinaryPrimitives.WriteInt32LittleEndian(scratch.AsSpan(offset * sizeof(int), sizeof(int)), bits);
            }
            var byteCount = checked(count * sizeof(int));
            work.Charge(byteCount);
            hash.AppendData(scratch, OffsetEmptyCount, byteCount);
            first += count;
        }
    }

    private static void AppendInt32(IncrementalHash hash, int value, AnnSeedWork work)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        work.Charge(bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value, AnnSeedWork work)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        work.Charge(bytes.Length);
        hash.AppendData(bytes);
    }
}
