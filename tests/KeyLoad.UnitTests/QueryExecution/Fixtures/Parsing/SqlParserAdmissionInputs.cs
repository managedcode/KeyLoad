namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlParserAdmissionInputs
{
    internal const string Secret = "PRIVATE-PARSER-ARGUMENT";
    internal const string Invalid = "The SQL syntax or query arguments are invalid.";
    internal const string Unsupported = "The SQL statement or execution clause is unsupported.";
    internal static IEnumerable<(string Sql, ErrorCode Code, string Detail)> Corpus()
    {
        const int LexicalCases = 32;
        const int FirstCase = 0;
        const int ExcessiveDepth = 17;
        const int ExcessiveConditions = 40;
        const int ExcessiveUtf8Characters = 2048;
        for (var index = FirstCase; index < LexicalCases; index++)
        {
            var suffix = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(
                global::System.Text.Encoding.UTF8.GetBytes("native-parser-" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture))));
            yield return ("SELECT title FROM offsetleft WHERE title = '" + Secret + suffix, ErrorCode.Validation, Invalid);
        }
        yield return ("SELECT title FROM offsetleft WHERE title = @" + Secret + " AND", ErrorCode.Validation, Invalid);
        yield return ("SELECT title FROM offsetleft WHERE " + new string('(', ExcessiveDepth) + "title = 'first'" + new string(')', ExcessiveDepth),
            ErrorCode.BudgetExceeded, "The SQL syntax exceeds its depth budget.");
        yield return ("SELECT title FROM offsetleft WHERE " + string.Join(" OR ", Enumerable.Repeat("title = 'first'", ExcessiveConditions)),
            ErrorCode.BudgetExceeded, "The SQL syntax exceeds its token budget.");
        yield return ("SELECT title FROM offsetleft WHERE title = '" + new string('界', ExcessiveUtf8Characters) + "'",
            ErrorCode.BudgetExceeded, "The SQL text exceeds its byte budget.");
        yield return ("SELECT DISTINCT title FROM offsetleft", ErrorCode.UnsupportedCapability, Unsupported);
        foreach (var sql in new[]
        {
            "SELECT TOP ((1 COLLATE Latin1_General_100_BIN2)) title FROM offsetleft ORDER BY order_id",
            "SELECT TOP (+1 COLLATE Latin1_General_100_BIN2) title FROM offsetleft ORDER BY order_id",
            "SELECT TOP (1) title FROM offsetleft WHERE -1 = -1 COLLATE Latin1_General_100_BIN2 ORDER BY order_id",
            "SELECT TOP (1) title FROM offsetleft ORDER BY 1 COLLATE Latin1_General_100_BIN2",
            "SELECT title FROM offsetleft ORDER BY order_id OFFSET ((1 COLLATE Latin1_General_100_BIN2)) ROWS FETCH NEXT 1 ROWS ONLY",
            "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1 COLLATE Latin1_General_100_BIN2) ORDER BY revision"
        })
        {
            yield return (sql, ErrorCode.UnsupportedCapability, Unsupported);
        }

    }
}
