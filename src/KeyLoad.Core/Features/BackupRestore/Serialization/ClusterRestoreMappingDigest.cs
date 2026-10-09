using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

/// <summary>Hashes exact ordered mapping fields independently of CLR graph reference aliasing.</summary>
public static class ClusterRestoreMappingDigest
{
    /// <summary>Measures every ordered typed mapping field using original fresh-session native scalar bytes.</summary>
    /// <param name="mappings">The complete first-admitted validated mapping array.</param>
    /// <returns>SHA256 over length-prefixed native scalar encodings in the frozen field order.</returns>
    public static string Compute(ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, mappings.Length);
        foreach (var mapping in mappings)
        {
            Append(hash, mapping.Version);
            AppendOwner(hash, mapping.Source);
            AppendOwner(hash, mapping.Target);
            Append(hash, mapping.Endpoints.Length);
            foreach (var endpoint in mapping.Endpoints)
            { Append(hash, endpoint); }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendOwner(IncrementalHash hash, PhysicalShardRecord owner)
    {
        Append(hash, owner.PhysicalShardId);
        Append(hash, owner.Incarnation);
        Append(hash, owner.PlacementEpoch);
        Append(hash, owner.VoterIds.Length);
        foreach (var voter in owner.VoterIds)
        { Append(hash, voter); }
    }

    private static void Append<T>(IncrementalHash hash, T value)
    {
        var bytes = NativeSerialization.Serialize(value);
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
