using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImagePackageDigestAssertions
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    internal static void Verify(string actual)
    {
        NativeCoverageImageOracleSupport.Ensure(NativeCoveragePackageDigestValidation.IsCanonicalSha512(actual),
            "The native materialized package digest is not canonical Base64 SHA-512.");
        var decoded = Convert.FromBase64String(actual);
        var alternateLastCharacter = Alphabet[Alphabet.IndexOf(actual[^3], StringComparison.Ordinal) + 1];
        var alternateEncoding = string.Concat(actual.AsSpan(0, actual.Length - 3),
            alternateLastCharacter.ToString(), "==");
        NativeCoverageImageOracleSupport.Ensure(Convert.FromBase64String(alternateEncoding).AsSpan().SequenceEqual(decoded),
            "The alternate encoding control must retain the entire observed digest.");
        foreach (var invalid in new[]
        {
            Convert.ToHexString(decoded), actual[..^1], actual.Insert(10, " "),
            string.Concat(actual.AsSpan(0, actual.Length - 2), "_="), alternateEncoding
        })
        {
            NativeCoverageImageOracleSupport.Ensure(!NativeCoveragePackageDigestValidation.IsCanonicalSha512(invalid),
                "A noncanonical native package digest was admitted.");
        }
        NativeCoverageImageOracleSupport.Ensure(NativeCoveragePackageDigestValidation.IsCanonicalSha512(actual),
            "The original native materialized digest must remain admissible after rejection.");
    }
}
