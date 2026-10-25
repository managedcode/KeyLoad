namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlDollarStringClusterParameters
{
    private const string Value = "'$1";
    private const string Predicate = "score = 1 AND label = N'low' AND N'''$1' = $1";

    internal static async Task RequireAsync(PostgresClusterCaller wire, SqlNullDistinctClusterCase item,
        long revision, CancellationToken token)
    {
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text,
                Value = Value
            };
            await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(() => new(SqlNullDistinctClusterCases.Prefix + Predicate + SqlNullDistinctClusterCases.Tail),
                [argument], prepared, token), item, revision);
        }
    }
}
