namespace KeyLoad.Comparisons.Targets;

public sealed partial class PostgresTarget : IBenchmarkSessionQueries
{
    private const string SessionIndexSql = "CREATE INDEX session_number_idx ON documents (((body->>'number')::integer)); ANALYZE documents";
    private const string SessionReadSql = "SELECT id,body::text FROM documents WHERE id=$1 AND (body->>'number')::integer >= $2 AND (body->>'number')::integer < $3 ORDER BY id LIMIT 2";
    private const string MissingSessionRow = "SessionNativeQueryMissingRow";
    private const string ExtraSessionRow = "SessionNativeQueryExtraRow";
    private const int SessionRangeWidth = 1;
    private const int SessionIdColumn = 0;
    private const int SessionPayloadColumn = 1;

    /// <inheritdoc/>
    public async Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SessionIndexSql;
        command.CommandTimeout = checked((int)Math.Ceiling(executionOptions.Value.IndexBuildTimeout.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SessionReadSql;
        command.Parameters.AddWithValue(expected.Id);
        command.Parameters.AddWithValue(expected.Number);
        command.Parameters.AddWithValue(expected.Number + SessionRangeWidth);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ComparisonFailureException(MissingSessionRow);
        }
        BenchmarkSessionQueryContract.Require(new(reader.GetString(SessionIdColumn), reader.GetString(SessionPayloadColumn)), expected);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ComparisonFailureException(ExtraSessionRow);
        }
    }
}
