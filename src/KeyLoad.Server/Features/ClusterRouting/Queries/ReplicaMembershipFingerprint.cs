using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipFingerprint
{
    private const string Domain = "keyload.orleans.membership.active.v1";
    private const string Invalid = "The active membership fingerprint input is invalid.";
    private const int ActiveCount = 6;
    private const int MaximumAddressBytes = 256;

    internal static string Compute(IEnumerable<string> activeAddresses)
    {
        ArgumentNullException.ThrowIfNull(activeAddresses);
        var addresses = activeAddresses.OrderBy(address => address, StringComparer.Ordinal).ToArray();
        if (addresses.Length != ActiveCount || addresses.Distinct(StringComparer.Ordinal).Count() != ActiveCount)
        { throw new ArgumentException(Invalid, nameof(activeAddresses)); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(Domain));
        AppendLength(hash, ActiveCount);
        foreach (var address in addresses)
        {
            if (string.IsNullOrEmpty(address))
            { throw new ArgumentException(Invalid, nameof(activeAddresses)); }
            var bytes = Encoding.UTF8.GetBytes(address);
            if (bytes.Length == 0 || bytes.Length > MaximumAddressBytes)
            { throw new ArgumentException(Invalid, nameof(activeAddresses)); }
            AppendLength(hash, bytes.Length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendLength(IncrementalHash hash, int value)
    {
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(length, checked((uint)value));
        hash.AppendData(length);
    }
}
