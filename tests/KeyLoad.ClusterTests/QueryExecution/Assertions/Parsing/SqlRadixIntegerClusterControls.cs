using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlRadixIntegerClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0B2" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0O8" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = -0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o100000000000000000000000000000000" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = -0o100000000000000000000000000000000" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b1.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o1.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b1_0.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o1_0.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b102" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o128" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "LIMIT 0b1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "LIMIT 0o1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "OFFSET 0b1 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "OFFSET 0o1 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "FETCH FIRST 0b1 ROW ONLY"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "FETCH FIRST 0o1 ROW ONLY"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "LIMIT +0b1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "OFFSET -0o1 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + "FETCH FIRST +0o1 ROW ONLY")
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlNullDistinctClusterCases.BinaryClause, SqlNullDistinctClusterCases.OctalClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, item.Command, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private const string OrderBy = " ORDER BY label ASC ";
}
