namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlDollarStringRf3Parameters
{
    private const string Value = "'$1";
    private const string Predicate = "score = 1 AND label = $tag$low$tag$ AND $other$'$1$other$ = $1";

    internal static async Task RequireAsync(PostgresRf3Caller wire, SqlNullDistinctRf3Case item,
        long revision, CancellationToken token)
    {
        var sql = SqlNullDistinctRf3Cases.Prefix + Predicate + SqlNullDistinctRf3Cases.Tail;
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text,
                Value = Value
            };
            await SqlNullDistinctRf3Image.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(sql,
                [argument], prepared, token), item, revision);
        }
    }
}
