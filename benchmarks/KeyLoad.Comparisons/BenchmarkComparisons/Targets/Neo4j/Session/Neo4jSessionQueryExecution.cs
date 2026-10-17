using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class Neo4jSessionQueryExecution
{
    private const string CreateIndexPrefix = "CREATE INDEX ";
    private const string IndexLabelClause = "_session FOR (n:";
    private const string IndexColumns = ") ON (n.id,n.json)";
    private const string AwaitIndexesStatement = "CALL db.awaitIndexes($seconds)";
    private const string MatchLabelPrefix = "MATCH (n:";
    private const string StableQuerySuffix = ") WHERE n.id=$id AND n.id >= $lower AND n.id < $upper RETURN n.id,n.json ORDER BY n.id LIMIT 2";
    private const string QueryCardinalityMismatch = "SessionNativeQueryCardinalityMismatch";
    private const int NextDocumentOffset = 1;
    private const int ExpectedRows = 1;
    private const int FirstRow = 0;
    private const int IdentityColumn = 0;
    private const int BodyColumn = 1;

    internal static async Task BuildIndexesAsync(string label, NativeComparisonExecutionOptions execution,
        Func<string, object?, CancellationToken, Task<JsonDocument>> query, CancellationToken cancellationToken)
    {
        using (var created = await query($"{CreateIndexPrefix}{label}{IndexLabelClause}{label}{IndexColumns}",
            null, cancellationToken).ConfigureAwait(false))
        {
            _ = created.RootElement;
        }
        using var ready = await query(AwaitIndexesStatement,
            new
            {
                seconds = checked((long)Math.Ceiling(execution.IndexBuildTimeout.TotalSeconds))
            }, cancellationToken).ConfigureAwait(false);
        _ = ready.RootElement;
    }

    internal static async Task QueryAsync(string label,
        Func<string, object?, CancellationToken, Task<JsonDocument>> query, int ordinal,
        int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        using var response = await query($"{MatchLabelPrefix}{label}{StableQuerySuffix}",
            new
            {
                id = expected.Id,
                lower = expected.Id,
                upper = ScaledComparisonCorpus.Id(expected.Number + NextDocumentOffset)
            }, cancellationToken).ConfigureAwait(false);
        var rows = DocumentNeo4jProtocol.Rows(response);
        if (rows.GetArrayLength() != ExpectedRows)
        {
            throw new ComparisonFailureException(QueryCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(rows[FirstRow][IdentityColumn].GetString()!,
            rows[FirstRow][BodyColumn].GetString()!), expected);
    }
}
