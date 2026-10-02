using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.Replication;

internal static class PeerDiscoverySignature
{
    internal static byte[] Compute(ReadOnlySpan<byte> secret, string method, string authority, string path, string timestamp, string nonce)
    {
        var message = string.Join(PeerDiscoveryProtocol.Separator, PeerDiscoveryProtocol.Purpose, method, authority, path, timestamp, nonce);
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(message));
    }

    internal static bool Read(HttpRequest request, out string timestamp, out Guid nonce, out string nonceText, out string signature)
    {
        timestamp = Single(request, PeerDiscoveryProtocol.TimeHeader, PeerDiscoveryProtocol.MaximumTimestampCharacters);
        nonceText = Single(request, PeerDiscoveryProtocol.NonceHeader, PeerDiscoveryProtocol.NonceCharacters);
        signature = Single(request, PeerDiscoveryProtocol.SignatureHeader, PeerDiscoveryProtocol.HashCharacters);
        return Guid.TryParseExact(nonceText, PeerDiscoveryProtocol.NonceFormat, out nonce) && nonce != Guid.Empty
            && signature.Length == PeerDiscoveryProtocol.HashCharacters && signature.All(Uri.IsHexDigit);
    }

    internal static bool Fresh(string timestamp, long now, out long time)
    {
        return long.TryParse(timestamp, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out time)
            && timestamp == time.ToString(CultureInfo.InvariantCulture)
            && time >= now - PeerDiscoveryProtocol.LifetimeMilliseconds && time <= now + PeerDiscoveryProtocol.LifetimeMilliseconds;
    }

    private static string Single(HttpRequest request, string header, int maximum)
    {
        var values = request.Headers[header];
        return values.Count == 1 && values[0] is { Length: > 0 } value && value.Length <= maximum ? value : string.Empty;
    }
}
