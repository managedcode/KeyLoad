using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class BenchmarkCorpusHash
{
    internal static string Compute(ImmutableArray<BenchmarkDocument> documents, ImmutableArray<BenchmarkEdge> edges)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var document in documents)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(document.Json));
            hash.AppendData(BenchmarkDataset.EventId(document).ToByteArray());
            foreach (var value in document.Vector)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                hash.AppendData(bytes);
            }
        }
        foreach (var edge in edges)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(edge)));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
