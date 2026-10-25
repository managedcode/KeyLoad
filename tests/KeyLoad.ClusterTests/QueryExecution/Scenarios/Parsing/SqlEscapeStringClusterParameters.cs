using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlEscapeStringClusterParameters
{
    private const string Value = "'$1";
    private const string ContinuedPredicate = "score = 1 AND label = N'low' AND N'''$1' = $1 /* named literal */";
    private const string Predicate = "score = 1 AND label = N'low' AND N'''$1' = $1";

    internal static async Task RequireAsync(PostgresClusterCaller wire, SqlNullDistinctClusterCase item,
        long revision, CancellationToken token)
    {
        foreach (var command in new Func<NpgsqlCommand>[] { () => new(SqlNullDistinctClusterCases.Prefix + Predicate + SqlNullDistinctClusterCases.Tail), () => new(SqlNullDistinctClusterCases.Prefix + ContinuedPredicate + SqlNullDistinctClusterCases.Tail) })
        {
            foreach (var prepared in new[] { false, true })
            {
                var argument = new global::Npgsql.NpgsqlParameter
                {
                    NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text,
                    Value = Value
                };
                await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(command,
                    [argument], prepared, token), item, revision);
            }
        }
    }
}
