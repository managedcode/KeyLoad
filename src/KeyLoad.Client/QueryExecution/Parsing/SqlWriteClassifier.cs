namespace KeyLoad.Client;

/// <summary>Classifies a SQL text as a read or a possible write from its leading keyword only; the server owns parsing
/// and authorization. The result selects the client error code when the transport fails before any reply arrives.</summary>
internal static class SqlWriteClassifier
{
    private const string Select = "SELECT";
    private const string Into = "INTO";
    private const string From = "FROM";
    private const string LineComment = "--";
    private const string BlockCommentStart = "/*";
    private const string BlockCommentEnd = "*/";
    private const int MaximumCommentNesting = 32;
    private const int NotFound = -1;
    private const int Start = 0;
    private const int SingleByte = 1;
    private const int TwoBytes = 2;
    private const int ThreeBytes = 3;
    private const int SurrogatePairBytes = 4;
    private const int TwoByteLimit = 0x80;
    private const int ThreeByteLimit = 0x800;
    private const char LineBreak = '\n';

    internal static bool MayWriteRequest(SqlOperationRequest request, KeyLoadClientExecutionOptions bounds)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MayWrite(request.Sql, bounds);
    }

    /// <summary>Scans the UTF-8 prefix that fits <see cref="KeyLoadClientExecutionOptions.MaximumSqlInspectionBytes"/>;
    /// anything that is not a complete leading SELECT inside that prefix is a possible write.</summary>
    internal static bool MayWrite(string? sql, KeyLoadClientExecutionOptions bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (sql is null)
        {
            return true;
        }
        var text = sql.AsSpan(Start, PrefixLength(sql, bounds.MaximumSqlInspectionBytes));
        var truncated = text.Length < sql.Length;
        if (!TrySkipTrivia(ref text))
        {
            return true;
        }
        var keyword = LeadingLetters(text);
        if (keyword.Length == text.Length && truncated || !keyword.Equals(Select, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return HasIntoBeforeFrom(text[keyword.Length..]);
    }

    private static int PrefixLength(string sql, int maximumBytes)
    {
        var bytes = Start;
        var index = Start;
        while (index < sql.Length)
        {
            var width = Utf8Width(sql, index);
            if (bytes + width > maximumBytes)
            {
                break;
            }
            bytes += width;
            index += width == SurrogatePairBytes ? TwoBytes : SingleByte;
        }
        return index;
    }

    private static int Utf8Width(string sql, int index)
    {
        if (char.IsHighSurrogate(sql[index]) && index + SingleByte < sql.Length && char.IsLowSurrogate(sql[index + SingleByte]))
        {
            return SurrogatePairBytes;
        }
        return sql[index] < TwoByteLimit ? SingleByte : sql[index] < ThreeByteLimit ? TwoBytes : ThreeBytes;
    }

    private static ReadOnlySpan<char> LeadingLetters(ReadOnlySpan<char> text)
    {
        var length = Start;
        while (length < text.Length && char.IsLetter(text[length]))
        {
            length++;
        }
        return text[..length];
    }

    /// <summary>Skips whitespace and T-SQL comments; returns false when a comment is unterminated or nested too deeply.</summary>
    private static bool TrySkipTrivia(ref ReadOnlySpan<char> text)
    {
        while (true)
        {
            text = text.TrimStart();
            if (text.StartsWith(LineComment, StringComparison.Ordinal))
            {
                var end = text.IndexOf(LineBreak);
                if (end == NotFound)
                {
                    text = default;
                    return true;
                }
                text = text[(end + SingleByte)..];
                continue;
            }
            if (!text.StartsWith(BlockCommentStart, StringComparison.Ordinal))
            {
                return true;
            }
            if (!TrySkipBlockComment(ref text))
            {
                return false;
            }
        }
    }

    private static bool TrySkipBlockComment(ref ReadOnlySpan<char> text)
    {
        var depth = Start;
        var index = Start;
        while (index < text.Length)
        {
            if (text[index..].StartsWith(BlockCommentStart, StringComparison.Ordinal))
            {
                if (++depth > MaximumCommentNesting)
                {
                    return false;
                }
                index += BlockCommentStart.Length;
                continue;
            }
            if (text[index..].StartsWith(BlockCommentEnd, StringComparison.Ordinal))
            {
                index += BlockCommentEnd.Length;
                if (--depth == Start)
                {
                    text = text[index..];
                    return true;
                }
                continue;
            }
            index++;
        }
        return false;
    }

    private static bool HasIntoBeforeFrom(ReadOnlySpan<char> text)
    {
        var into = IndexOfWord(text, Into);
        var from = IndexOfWord(text, From);
        return into != NotFound && (from == NotFound || into < from);
    }

    private static int IndexOfWord(ReadOnlySpan<char> text, string word)
    {
        var offset = Start;
        while (true)
        {
            var index = text[offset..].IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index == NotFound)
            {
                return NotFound;
            }
            var position = offset + index;
            var end = position + word.Length;
            if ((position == Start || !char.IsLetterOrDigit(text[position - SingleByte]))
                && (end == text.Length || !char.IsLetterOrDigit(text[end])))
            {
                return position;
            }
            offset = position + SingleByte;
        }
    }
}
