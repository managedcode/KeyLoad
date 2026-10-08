using System.Security.Cryptography;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalDigest
{
    private const int HexCharactersPerByte = 2;
    private const char FirstDecimalDigit = '0';
    private const char LastDecimalDigit = '9';
    private const char FirstLowerHexLetter = 'a';
    private const char LastLowerHexLetter = 'f';

    internal static bool IsCanonical(string? value)
    {
        if (value is null || value.Length != SHA256.HashSizeInBytes * HexCharactersPerByte)
        { return false; }
        foreach (var character in value)
        {
            if (character is not (>= FirstDecimalDigit and <= LastDecimalDigit) and not (>= FirstLowerHexLetter and <= LastLowerHexLetter))
            { return false; }
        }
        return true;
    }
}
