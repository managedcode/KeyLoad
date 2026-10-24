namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlEscapeStringClusterParameters
{
    private const string Value = "'$1";
    private const string ContinuedPredicate = "score = 1 AND label = 'lo'\n'w' AND E'\\'$1'\n'' = $1";
    private const string Predicate = "score = 1 AND label = E'low' AND E'\\'$1' = $1";

    internal static async Task RequireAsync(PostgresClusterCaller wire, SqlNullDistinctClusterCase item,
        long revision, CancellationToken token)
    {
        foreach (var predicate in new[] { Predicate, ContinuedPredicate })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + predicate + SqlNullDistinctClusterCases.Tail;
            foreach (var prepared in new[] { false, true })
            {
                var argument = new global::Npgsql.NpgsqlParameter
                {
                    NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text,
                    Value = Value
                };
                await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(sql,
                    [argument], prepared, token), item, revision);
            }
        }
    }
}
