using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnSeedDigestOracle
{
    private const string Domain = "keyload.ann.seed.corpus.v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Compute(string field, VectorSpace space, IReadOnlyList<VectorRecord> records)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, Domain);
        AppendText(hash, field);
        AppendText(hash, space.Id);
        AppendInt32(hash, space.Dimension);
        AppendInt32(hash, (int)space.Metric);
        AppendText(hash, space.Model);
        AppendText(hash, space.Version);
        AppendInt32(hash, records.Count);
        foreach (var record in records)
        {
            AppendText(hash, record.DocumentId);
            AppendInt64(hash, record.DocumentRevision);
            foreach (var component in record.Values)
            {
                AppendInt32(hash, BitConverter.SingleToInt32Bits(component));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
