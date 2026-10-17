using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerProbeNativeIdentity
{
    private const char UrlDash = '-';
    private const char UrlUnderscore = '_';
    private const char Base64Plus = '+';
    private const char Base64Slash = '/';
    private const char Base64Padding = '=';
    private const string NoncePadding = "==";
    private const int AmbiguousMatchCount = 2;
    private const int UniqueMatchCount = 1;
    private const int FirstMatchIndex = 0;
    internal const int NonceBytes = 16;
    private const int NonceCharacters = 22;
    internal static bool ValidNonce(string? value)
    {
        if (value is not { Length: NonceCharacters }
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not UrlDash and not UrlUnderscore))
        {
            return false;
        }
        Span<byte> bytes = stackalloc byte[NonceBytes];
        var encoded = value.Replace(UrlDash, Base64Plus).Replace(UrlUnderscore, Base64Slash) + NoncePadding;
        return Convert.TryFromBase64String(encoded, bytes, out var count) && count == bytes.Length
            && string.Equals(Convert.ToBase64String(bytes).TrimEnd(Base64Padding).Replace(Base64Plus, UrlDash).Replace(Base64Slash, UrlUnderscore),
                value, StringComparison.Ordinal);
    }
    internal static SiloAddress Resolve(OrleansNode node, string voter, IOptions<OrleansMembershipOptions> membership)
    {
        var matches = node.ReadActiveNativeMembership().Where(row => row.SiloName == voter).Take(AmbiguousMatchCount).ToArray();
        if (matches.Length != UniqueMatchCount || !PhysicalGroupAddress.CanonicalAddress(matches[FirstMatchIndex].SiloAddress, membership))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        }
        return SiloAddress.FromParsableString(matches[FirstMatchIndex].SiloAddress);
    }
    internal static void RequireCurrent(OrleansNode node, string voter, string address,
        IOptions<OrleansMembershipOptions> membership, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PhysicalGroupAddress.CanonicalAddress(address, membership)
            || Resolve(node, voter, membership).ToParsableString() != address)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
    }
}
