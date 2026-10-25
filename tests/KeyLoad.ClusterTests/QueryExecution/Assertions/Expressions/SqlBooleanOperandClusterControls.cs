using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanOperandClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlBooleanTruthClusterCases.Prefix + "score TRUE" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "score =" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "1" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "'true'" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "@absent" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "NOT" + SqlBooleanTruthClusterCases.Tail)
        })
        {
            await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, ErrorCode code, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        foreach (var item in SqlBooleanTruthClusterCases.All.Where(item => item.Clause == SqlBooleanTruthClusterCases.BareClause
            || item.Clause == SqlBooleanTruthClusterCases.NegatedBareClause))
        {
            await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, state, item.Command, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
