namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Preserves bounded RFC 6901 pointer encoding and decoding for shared field operations.</summary>
internal static class JsonPointerPaths
{
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;
    private const int FirstElementIndex = 0;

    private const int MaximumCharacters = 1_024;
    private const char Separator = '/';
    private const char Escape = '~';
    private const char EscapedTildeCode = '0';
    private const char EscapedSeparatorCode = '1';
    private const string SeparatorText = "/";
    private const string EscapeText = "~";
    private const string EscapedTilde = "~0";
    private const string EscapedSeparator = "~1";
    private const string InvalidPointer = "Field paths must be bounded JSON pointers.";
    private const string InvalidEscaping = "A field path contains invalid escaping.";

    internal static string[] Parse(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        if (pointer.Length == EmptyElementCount)
        {
            return [];
        }
        if (!pointer.StartsWith(Separator) || pointer.Length > MaximumCharacters)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPointer);
        }
        var parts = pointer[AdjacentElementOffset..].Split(Separator);
        foreach (var part in parts)
        {
            ValidateEscaping(part);
        }
        return parts.Select(part => part.Replace(EscapedSeparator, SeparatorText, StringComparison.Ordinal)
            .Replace(EscapedTilde, EscapeText, StringComparison.Ordinal)).ToArray();
    }

    internal static string Encode(string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return SeparatorText + string.Join(Separator, segments.Select(segment =>
            segment.Replace(EscapeText, EscapedTilde, StringComparison.Ordinal)
                .Replace(SeparatorText, EscapedSeparator, StringComparison.Ordinal)));
    }

    private static void ValidateEscaping(string part)
    {
        for (var index = FirstElementIndex; index < part.Length; index++)
        {
            if (part[index] == Escape && (++index == part.Length
                    || part[index] is not (EscapedTildeCode or EscapedSeparatorCode)))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEscaping);
            }
        }
    }
}
