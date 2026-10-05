using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.Replication;

internal static class PeerDiscoverySignature
{
    private const int SingleHeaderValue = 1;
    private const int FirstHeaderValueIndex = 0;
    private const int EmptyHeaderValueLength = 0;

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

    internal static bool Fresh(string timestamp, long now, long timestampWindowMilliseconds, out long time)
    {
        return long.TryParse(timestamp, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out time)
            && timestamp == time.ToString(CultureInfo.InvariantCulture)
            && time >= now - timestampWindowMilliseconds && time <= now + timestampWindowMilliseconds;
    }

    private static string Single(HttpRequest request, string header, int maximum)
    {
        var values = request.Headers[header];
        return values.Count == SingleHeaderValue && values[FirstHeaderValueIndex] is { Length: > EmptyHeaderValueLength } value && value.Length <= maximum ? value : string.Empty;
    }
}
