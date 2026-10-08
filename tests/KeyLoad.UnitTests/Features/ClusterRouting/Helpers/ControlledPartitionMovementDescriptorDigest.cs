using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Hashes independently expected original families and literal resource metadata using the frozen image contract.</summary>
internal static class ControlledPartitionMovementDescriptorDigest
{
    private const int LengthBytes = sizeof(long);

    internal static string Of(IEnumerable<PartitionMoveImageFamily> expectedFamilies)
    {
        byte[] familyDigest;
        using (var families = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            foreach (var family in expectedFamilies)
            {
                Bytes(families, Encoding.UTF8.GetBytes(family.Family));
                Number(families, family.RecordCount);
                Number(families, family.RawBytes);
                Number(families, family.PageCount);
                Bytes(families, Convert.FromHexString(family.Digest));
            }
            familyDigest = families.GetHashAndReset();
        }
        using var image = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Bytes(image, familyDigest);
        var literalResources = ControlledPartitionMovementResourceAssertions.Expected()
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToImmutableArray();
        Bytes(image, Encoding.UTF8.GetBytes(JsonData.Fingerprint(literalResources)));
        return Convert.ToHexStringLower(image.GetHashAndReset());
    }

    private static void Bytes(IncrementalHash hash, byte[] bytes)
    {
        Number(hash, bytes.LongLength);
        hash.AppendData(bytes);
    }

    private static void Number(IncrementalHash hash, long value)
    {
        Span<byte> number = stackalloc byte[LengthBytes];
        BinaryPrimitives.WriteInt64BigEndian(number, value);
        hash.AppendData(number);
    }
}
