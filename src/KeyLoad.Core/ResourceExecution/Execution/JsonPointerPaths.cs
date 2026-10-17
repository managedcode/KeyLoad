using System.Globalization;

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
    private const char ArrayIndexZeroDigit = '0';
    private const char ArrayIndexNineDigit = '9';
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

    internal static bool TryArrayIndex(string segment, out int index)
    {
        ArgumentNullException.ThrowIfNull(segment);
        index = FirstElementIndex;
        if (segment.Length == EmptyElementCount
            || segment.Length > AdjacentElementOffset && segment[FirstElementIndex] == ArrayIndexZeroDigit)
        {
            return false;
        }
        foreach (var character in segment)
        {
            if (character is < ArrayIndexZeroDigit or > ArrayIndexNineDigit)
            {
                return false;
            }
        }
        return int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index);
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
