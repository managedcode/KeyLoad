namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlDollarStringClusterParameters
{
    private const string Value = "'$1";
    private const string Predicate = "score = 1 AND label = $tag$low$tag$ AND $other$'$1$other$ = $1";

    internal static async Task RequireAsync(PostgresClusterCaller wire, SqlNullDistinctClusterCase item,
        long revision, CancellationToken token)
    {
        var sql = SqlNullDistinctClusterCases.Prefix + Predicate + SqlNullDistinctClusterCases.Tail;
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
