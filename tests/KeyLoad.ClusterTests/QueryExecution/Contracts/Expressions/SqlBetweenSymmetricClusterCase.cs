using KeyLoad.Query;
using Npgsql;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlBetweenSymmetricClusterCase(Func<NpgsqlCommand> Command, Predicate Predicate, string[] Ids)
{
    internal string Sql => PostgresClusterCommands.Sql(Command);
    internal string Clause => Sql[SqlBetweenSymmetricClusterCases.Prefix.Length..^SqlBetweenSymmetricClusterCases.Tail.Length];
}
