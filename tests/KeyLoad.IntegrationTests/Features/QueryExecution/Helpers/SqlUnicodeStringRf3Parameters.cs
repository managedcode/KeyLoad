namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeStringRf3Parameters
{
    private const string Value = "'$1";
    private const string Predicate = "score = 1 AND label = U&'l\\006fw' AND U&'\\0027$1' = $1";
    private const string CustomPredicate = "score = 1 AND label = U&'l#006fw' UESCAPE '#' AND U&'#0027$1' UESCAPE '#' = $1";

    internal static async Task RequireAsync(PostgresRf3Caller wire, SqlNullDistinctRf3Case item,
        long revision, CancellationToken token)
    {
        foreach (var predicate in new[] { Predicate, CustomPredicate })
        {
            var sql = SqlNullDistinctRf3Cases.Prefix + predicate + SqlNullDistinctRf3Cases.Tail;
            foreach (var prepared in new[] { false, true })
            {
                var argument = new global::Npgsql.NpgsqlParameter
                { NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text, Value = Value };
                await SqlNullDistinctRf3Image.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(sql,
                    [argument], prepared, token), item, revision);
            }
        }
    }
}
