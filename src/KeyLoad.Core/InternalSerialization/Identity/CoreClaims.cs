using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Core.InternalSerialization;

internal static class CoreClaims
{
    internal const string Prefix = "KLT2.";
    private const string InvalidToken = "The signed token is invalid.";
    private const char Separator = '.';
    private const char Padding = '=';
    private const char StandardPlus = '+';
    private const char StandardSlash = '/';
    private const char UrlMinus = '-';
    private const char UrlUnderscore = '_';
    private const int Base64Quantum = 4;
    private const int TokenParts = 2;
    private static readonly byte[] PrefixBytes = Encoding.UTF8.GetBytes(Prefix);

    internal static string Sign<T>(ReadOnlySpan<byte> key, T claims)
    {
        var bytes = OrleansSerialization.Serialize(claims);
        return Prefix + Base64Url(bytes) + Separator + Base64Url(Signature(key, bytes));
    }

    // Exact owner-execution framing count only: no signature or admission is created.
    internal static int MeasureCharacters<T>(T claims)
    {
        const long Base64InputQuantum = 3;
        const long QuantumRoundingAdjustment = 2;
        const int SeparatorCharacters = 1;
        var bytes = OrleansSerialization.Measure(claims);
        var payload = checked((bytes * Base64Quantum + QuantumRoundingAdjustment) / Base64InputQuantum);
        var signature = (SHA256.HashSizeInBytes * Base64Quantum + QuantumRoundingAdjustment) / Base64InputQuantum;
        return checked((int)(Prefix.Length + payload + SeparatorCharacters + signature));
    }

    internal static T Verify<T>(ReadOnlySpan<byte> key, string token, int maximumCharacters)
    {
        const int PayloadSegmentIndex = 0;
        const int SignatureSegmentIndex = 1;

        try
        {
            var parts = Segments(token, maximumCharacters);
            var bytes = Convert.FromBase64String(Pad(parts[PayloadSegmentIndex]));
            var mac = Convert.FromBase64String(Pad(parts[SignatureSegmentIndex]));
            if (!CryptographicOperations.FixedTimeEquals(Signature(key, bytes), mac))
            {
                throw new FormatException();
            }
            return OrleansSerialization.Deserialize<T>(bytes);
        }
        catch (FormatException)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidToken);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidToken);
        }
    }

    /// <summary>Reads the claims a token carries without checking its signature. The owning database verifies the
    /// signature when the token is applied; callers derive identities from a witness that owner handed them over Orleans.</summary>
    internal static T Decode<T>(string token, int maximumCharacters)
    {
        const int PayloadSegmentIndex = 0;

        try
        {
            return OrleansSerialization.Deserialize<T>(Convert.FromBase64String(Pad(Segments(token, maximumCharacters)[PayloadSegmentIndex])));
        }
        catch (FormatException)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidToken);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidToken);
        }
    }

    private static string[] Segments(string token, int maximumCharacters)
    {
        const int MinimumTokenCharacters = 1;

        ArgumentNullException.ThrowIfNull(token);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, MinimumTokenCharacters);
        if (token.Length > maximumCharacters || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException();
        }
        var parts = token[Prefix.Length..].Split(Separator);
        return parts.Length == TokenParts ? parts : throw new FormatException();
    }

    private static byte[] Signature(ReadOnlySpan<byte> key, ReadOnlySpan<byte> bytes)
    {
        const int PrefixByteOffset = 0;

        var authenticated = new byte[checked(PrefixBytes.Length + bytes.Length)];
        PrefixBytes.CopyTo(authenticated, PrefixByteOffset);
        bytes.CopyTo(authenticated.AsSpan(PrefixBytes.Length));
        return HMACSHA256.HashData(key, authenticated);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd(Padding).Replace(StandardPlus, UrlMinus).Replace(StandardSlash, UrlUnderscore);
    private static string Pad(string value)
    {
        const int QuantumRoundingAdjustment = 1;

        var padded = value.Replace(UrlMinus, StandardPlus).Replace(UrlUnderscore, StandardSlash);
        return padded.PadRight((padded.Length + Base64Quantum - QuantumRoundingAdjustment) / Base64Quantum * Base64Quantum, Padding);
    }
}
