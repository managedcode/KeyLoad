using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

internal static class PartitionMoveImageDigest
{
    private const int LengthBytes = sizeof(long);

    internal static string Records(IEnumerable<KeyValueRecord> records)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var record in records)
        {
            Append(hash, record.Key.Span);
            Append(hash, record.Value.Span);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string Families(IEnumerable<PartitionMoveImageFamily> families)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var family in families)
        {
            Append(hash, Encoding.UTF8.GetBytes(family.Family));
            AppendNumber(hash, family.RecordCount);
            AppendNumber(hash, family.RawBytes);
            AppendNumber(hash, family.PageCount);
            Append(hash, Convert.FromHexString(family.Digest));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string Image(IEnumerable<PartitionMoveImageFamily> families,
        System.Collections.Immutable.ImmutableArray<ResourceDefinition> resources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Convert.FromHexString(Families(families)));
        Append(hash, Encoding.UTF8.GetBytes(JsonData.Fingerprint(resources)));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        AppendNumber(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendNumber(IncrementalHash hash, long number)
    {
        Span<byte> encoded = stackalloc byte[LengthBytes];
        BinaryPrimitives.WriteInt64BigEndian(encoded, number);
        hash.AppendData(encoded);
    }
}
