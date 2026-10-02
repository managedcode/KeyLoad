using System.Globalization;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validates bounded registry, repository, tag and immutable digest references.</summary>
internal static class ComparisonExecutionIdentityImageReference
{
    private const string Sha256Prefix = "sha256:";
    private const string CurrentDirectorySegment = ".";
    private const string ParentDirectorySegment = "..";
    private const char ImageDigestSeparator = '@';
    private const char RegistryPathSeparator = '/';
    private const char RegistryLabelSeparator = '.';
    private const char TagSeparator = ':';
    private const char RepositorySegmentSeparator = '-';
    private const int Sha256HexLength = 64;
    private const int MaximumImageReferenceLength = 1_024;
    private const int MaximumRegistryPort = 65_535;

    /// <summary>Checks for a complete registry/path:tag@sha256 reference with bounded input length.</summary>
    /// <param name="value">The configured image reference.</param>
    /// <returns>True only for a syntactically complete immutable reference.</returns>
    internal static bool IsValid(string value)
    {
        if (value.Length is 0 or > MaximumImageReferenceLength)
        {
            return false;
        }

        var digestSeparator = value.LastIndexOf(ImageDigestSeparator);
        if (digestSeparator <= 0 || value.IndexOf(ImageDigestSeparator, StringComparison.Ordinal) != digestSeparator)
        {
            return false;
        }

        var digest = value.AsSpan(digestSeparator + 1);
        if (digest.Length != Sha256Prefix.Length + Sha256HexLength
            || !digest.StartsWith(Sha256Prefix, StringComparison.Ordinal)
            || !IsLowercaseSha256(digest[Sha256Prefix.Length..]))
        {
            return false;
        }

        var taggedImage = value.AsSpan(0, digestSeparator);
        var pathSeparator = taggedImage.IndexOf(RegistryPathSeparator);
        var tagSeparator = taggedImage.LastIndexOf(TagSeparator);
        if (pathSeparator <= 0 || tagSeparator <= pathSeparator || tagSeparator >= taggedImage.Length - 1)
        {
            return false;
        }

        return IsValidRegistry(taggedImage[..pathSeparator])
            && IsValidRepositoryPath(taggedImage[(pathSeparator + 1)..tagSeparator])
            && IsValidTag(taggedImage[(tagSeparator + 1)..]);
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
        if (portSeparator >= 0)
        {
            if (registry.IndexOf(TagSeparator) != portSeparator
                || !int.TryParse(registry[(portSeparator + 1)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var port)
                || port is < 1 or > MaximumRegistryPort)
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
        => label.Length > 0
            && IsLowercaseAlphaNumeric(label[0])
            && IsLowercaseAlphaNumeric(label[^1])
            && label.All(static character => IsLowercaseAlphaNumeric(character) || character == '-');

    private static bool IsValidRepositoryPath(ReadOnlySpan<char> path)
        => path.Length > 0 && path.ToString().Split(RegistryPathSeparator).All(IsValidRepositorySegment);

    private static bool IsValidRepositorySegment(string segment)
        => segment.Length > 0
            && segment is not CurrentDirectorySegment and not ParentDirectorySegment
            && segment.All(static character => IsLowercaseAlphaNumeric(character)
                || character is '.' or '_' or RepositorySegmentSeparator);

    private static bool IsValidTag(ReadOnlySpan<char> tag)
        => tag.Length is > 0 and <= 128
            && IsTagStartCharacter(tag[0])
            && tag.ToString().All(IsTagCharacter);

    private static bool IsTagStartCharacter(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    private static bool IsLowercaseAlphaNumeric(char character)
        => character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsLowercaseHex(char character)
        => character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool IsTagCharacter(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
}
