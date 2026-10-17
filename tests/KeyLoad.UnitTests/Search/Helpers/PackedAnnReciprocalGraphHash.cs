using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnReciprocalGraphHash
{
    internal static string Compute(PackedAnnState state, int[][][] adjacency)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("keyload.ann.graph.v1"u8);
        AppendInt(hash, state.Count);
        AppendInt(hash, state.EntryPoint);
        AppendInt(hash, state.MaximumLevel);
        for (var source = 0; source < state.Count; source++)
        {
            var id = Encoding.UTF8.GetBytes(state.Ids[source]);
            AppendInt(hash, id.Length);
            hash.AppendData(id);
            AppendLong(hash, state.Revisions[source]);
            AppendInt(hash, state.Levels[source]);
            AppendInt(hash, adjacency[source].Length);
            foreach (var neighbors in adjacency[source])
            {
                AppendInt(hash, neighbors.Length);
                foreach (var neighbor in neighbors)
                {
                    AppendInt(hash, neighbor);
                }
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendInt(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendLong(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
