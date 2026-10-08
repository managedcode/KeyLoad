using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Hashes the independently captured ordered key/value reference using the frozen wire length framing.</summary>
internal static class ControlledPartitionMovementRecordDigest
{
    private const int SeparatorCharacters = 1;
    private const int InvalidSeparator = 0;
    private const int LengthBytes = sizeof(long);

    internal static string Of(IEnumerable<string> originalRows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in originalRows)
        {
            var separator = row.IndexOf(':', StringComparison.Ordinal);
            if (separator <= InvalidSeparator)
            { throw new InvalidOperationException("The original raw reference is malformed."); }
            Append(hash, Convert.FromHexString(row[..separator]));
            Append(hash, Convert.FromHexString(row[(separator + SeparatorCharacters)..]));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[LengthBytes];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
