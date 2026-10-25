using KeyLoad.Query;
using Npgsql;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlNullDistinctClusterCase(Func<NpgsqlCommand> Command, Predicate Predicate, string[] Ids)
{
    internal string Sql => PostgresClusterCommands.Sql(Command);
    internal string Clause => Sql[SqlNullDistinctClusterCases.Prefix.Length..^SqlNullDistinctClusterCases.Tail.Length];
}
