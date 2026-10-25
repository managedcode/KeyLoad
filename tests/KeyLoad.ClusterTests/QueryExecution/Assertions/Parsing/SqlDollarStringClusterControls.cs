using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlDollarStringClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "label = $tag$unfinished" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = $TAG$low$tag$" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = $tag-low$low$tag-low$" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "label = $$lo$$\n$$w$$" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = $a$low$a$ $a$other$a$" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
