using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validates bounded registry, repository, tag and immutable digest references.</summary>
internal static class ComparisonExecutionIdentityImageReference
{
    private const char AsciiDigitEnd = '9';
    private const char AsciiDigitStart = '0';
    private const int EmptySegmentLength = 0;
    private const int FirstCharacterIndex = 0;
    private const char HexadecimalLetterEnd = 'f';
    private const int LastCharacterOffset = 1;
    private const char LowercaseAsciiEnd = 'z';
    private const char LowercaseAsciiStart = 'a';
    private const int MaximumOciTagCharacters = 128;
    private const int MinimumRegistryPort = 1;
    private const int SeparatorWidth = 1;
    private const char UnderscoreCharacter = '_';
    private const char UppercaseAsciiEnd = 'Z';
    private const char UppercaseAsciiStart = 'A';
    private const string Sha256Prefix = "sha256:";
    private const string CurrentDirectorySegment = ".";
    private const string ParentDirectorySegment = "..";
    private const char ImageDigestSeparator = '@';
    private const char RegistryPathSeparator = '/';
    private const char RegistryLabelSeparator = '.';
    private const char TagSeparator = ':';
    private const char RepositorySegmentSeparator = '-';
    private const int Sha256HexLength = 64;
    private const int MaximumRegistryPort = 65_535;

    /// <summary>Checks for a complete registry/path:tag@sha256 reference with bounded input length.</summary>
    /// <param name="value">The configured image reference.</param>
    /// <param name="executionOptions">Centrally validated input length policy.</param>
    /// <returns>True only for a syntactically complete immutable reference.</returns>
    internal static bool IsValid(string value, IOptions<ComparisonHostExecutionOptions> executionOptions)
    {
        if (value.Length == EmptySegmentLength || value.Length > executionOptions.Value.MaximumImageReferenceCharacters)
        {
            return false;
        }

        var digestSeparator = value.LastIndexOf(ImageDigestSeparator);
        if (digestSeparator <= FirstCharacterIndex || value.IndexOf(ImageDigestSeparator, StringComparison.Ordinal) != digestSeparator)
        {
            return false;
        }

        var digest = value.AsSpan(digestSeparator + SeparatorWidth);
        if (digest.Length != Sha256Prefix.Length + Sha256HexLength
            || !digest.StartsWith(Sha256Prefix, StringComparison.Ordinal)
            || !IsLowercaseSha256(digest[Sha256Prefix.Length..]))
        {
            return false;
        }

        var taggedImage = value.AsSpan(FirstCharacterIndex, digestSeparator);
        var pathSeparator = taggedImage.IndexOf(RegistryPathSeparator);
        var tagSeparator = taggedImage.LastIndexOf(TagSeparator);
        if (pathSeparator <= FirstCharacterIndex || tagSeparator <= pathSeparator || tagSeparator >= taggedImage.Length - LastCharacterOffset)
        {
            return false;
        }

        return IsValidRegistry(taggedImage[..pathSeparator])
            && IsValidRepositoryPath(taggedImage[(pathSeparator + SeparatorWidth)..tagSeparator])
            && IsValidTag(taggedImage[(tagSeparator + SeparatorWidth)..]);
    }

    private static bool IsLowercaseSha256(ReadOnlySpan<char> digest)
    {
        foreach (var character in digest)
        {
            if (!IsLowercaseHex(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidRegistry(ReadOnlySpan<char> registry)
    {
        var portSeparator = registry.LastIndexOf(TagSeparator);
        if (portSeparator >= FirstCharacterIndex)
        {
            if (registry.IndexOf(TagSeparator) != portSeparator
                || !int.TryParse(registry[(portSeparator + SeparatorWidth)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var port)
                || port is < MinimumRegistryPort or > MaximumRegistryPort)
            {
                return false;
            }

            registry = registry[..portSeparator];
        }

        return IsValidRegistryHost(registry);
    }

    private static bool IsValidRegistryHost(ReadOnlySpan<char> host)
    {
        return host.ToString().Split(RegistryLabelSeparator).All(IsValidRegistryLabel);
    }

    private static bool IsValidRegistryLabel(string label)
        => label.Length > EmptySegmentLength
            && IsLowercaseAlphaNumeric(label[FirstCharacterIndex])
            && IsLowercaseAlphaNumeric(label[^LastCharacterOffset])
            && label.All(static character => IsLowercaseAlphaNumeric(character) || character == RepositorySegmentSeparator);

    private static bool IsValidRepositoryPath(ReadOnlySpan<char> path)
        => path.Length > EmptySegmentLength && path.ToString().Split(RegistryPathSeparator).All(IsValidRepositorySegment);

    private static bool IsValidRepositorySegment(string segment)
        => segment.Length > EmptySegmentLength
            && segment is not CurrentDirectorySegment and not ParentDirectorySegment
            && segment.All(static character => IsLowercaseAlphaNumeric(character)
                || character is RegistryLabelSeparator or UnderscoreCharacter or RepositorySegmentSeparator);

    private static bool IsValidTag(ReadOnlySpan<char> tag)
        => tag.Length is > EmptySegmentLength and <= MaximumOciTagCharacters
            && IsTagStartCharacter(tag[FirstCharacterIndex])
            && tag.ToString().All(IsTagCharacter);

    private static bool IsTagStartCharacter(char character)
        => character is >= LowercaseAsciiStart and <= LowercaseAsciiEnd or >= UppercaseAsciiStart and <= UppercaseAsciiEnd or >= AsciiDigitStart and <= AsciiDigitEnd or UnderscoreCharacter;

    private static bool IsLowercaseAlphaNumeric(char character)
        => character is >= LowercaseAsciiStart and <= LowercaseAsciiEnd or >= AsciiDigitStart and <= AsciiDigitEnd;

    private static bool IsLowercaseHex(char character)
        => character is >= AsciiDigitStart and <= AsciiDigitEnd or >= LowercaseAsciiStart and <= HexadecimalLetterEnd;

    private static bool IsTagCharacter(char character)
        => character is >= LowercaseAsciiStart and <= LowercaseAsciiEnd or >= UppercaseAsciiStart and <= UppercaseAsciiEnd or >= AsciiDigitStart and <= AsciiDigitEnd or RegistryLabelSeparator or UnderscoreCharacter or RepositorySegmentSeparator;
}
