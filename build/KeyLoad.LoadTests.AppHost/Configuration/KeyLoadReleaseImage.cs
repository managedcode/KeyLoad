namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>The Release-published KeyLoad server image, resolved once per benchmark run to one immutable digest.</summary>
internal sealed record KeyLoadReleaseImage(string Tag, string Digest)
{
    internal const string Repository = "ghcr.io/managedcode/keyload";
    internal const string Invalid = "The KeyLoad benchmark requires Benchmarks:KeyLoadImage as ghcr.io/managedcode/keyload:<tag>@sha256:<digest>.";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@sha256:";
    private const int DigestCharacters = 64;
    private const int MaximumTagCharacters = 128;
    private const char TagPunctuationDot = '.';
    private const char TagPunctuationDash = '-';
    private const char TagUnderscore = '_';

    internal string Reference => Repository + TagSeparator + Tag + DigestSeparator + Digest;

    internal static bool IsValid(string? value) => (TryParse(value)).Value is not null;

    internal static KeyLoadReleaseImage Parse(string? value) => (TryParse(value)).Value ?? throw new InvalidOperationException(Invalid);

    private static global::ManagedCode.Communication.Result<KeyLoadReleaseImage?> TryParse(string? value)
    {
        const string Prefix = Repository + TagSeparator;
        const int Missing = -1;

        var separator = value?.IndexOf(DigestSeparator, StringComparison.Ordinal) ?? Missing;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal) || separator <= Prefix.Length)
        {
            return global::ManagedCode.Communication.Result<KeyLoadReleaseImage?>.FailNotFound(nameof(TryParse));
        }
        var tag = value[Prefix.Length..separator];
        var digest = value[(separator + DigestSeparator.Length)..];
        return tag.Length <= MaximumTagCharacters && char.IsAsciiLetterOrDigit(tag[0]) && tag.All(IsTagCharacter)
            && digest.Length == DigestCharacters && digest.All(char.IsAsciiHexDigitLower)
            ? global::ManagedCode.Communication.Result<KeyLoadReleaseImage?>.Succeed((KeyLoadReleaseImage?)(new(tag, digest))) : global::ManagedCode.Communication.Result<KeyLoadReleaseImage?>.FailNotFound(nameof(TryParse));
    }

    private static bool IsTagCharacter(char value) => char.IsAsciiLetterOrDigit(value)
        || value is TagPunctuationDot or TagPunctuationDash or TagUnderscore;
}
