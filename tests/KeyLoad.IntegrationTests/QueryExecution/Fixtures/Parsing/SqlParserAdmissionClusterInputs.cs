namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlParserAdmissionClusterInputs
{
    internal const string Invalid = "The SQL syntax or query arguments are invalid.";
    internal const string Unsupported = "The SQL statement or execution clause is unsupported.";
    internal static IEnumerable<(string Sql, ErrorCode Code, string Detail)> Corpus()
    {
        const int LexicalCases = 4;
        for (var index = 0; index < LexicalCases; index++)
        {
            yield return ("SELECT title FROM offsetleft WHERE title = '" + SqlParserAdmissionClusterState.Secret
                + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture), ErrorCode.Validation, Invalid);
        }
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
