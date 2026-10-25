using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNumericSeparatorClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1_" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1__0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = .1_" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1_.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1._0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1e_1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1_e1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 1e+_1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0x__1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0x1_" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0b_" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o1__0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = + 0b_1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 79_228_162_514_264_337_593_543_950_336" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0x_1_0000_0000_0000_0000_0000_0000" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0o_1_0000_0000_0000_0000_0000_0000_0000_0000" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT 0_0"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT 2_147_483_648"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "OFFSET 2_147_483_648 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "FETCH FIRST 2_147_483_648 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT +0_1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "OFFSET -0_1 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "FETCH FIRST 0x_1 ROW ONLY"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT 0b_1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "OFFSET 0o_1 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT 1_0e-1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "FETCH FIRST 1.0_0 ROW ONLY")
        })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "LIMIT 0_1 OFFSET 0_0 LIMIT 1"),
            () => new(SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "FETCH FIRST 0_1 ROW ONLY LIMIT 1")
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
        foreach (var clause in new[] { SqlNullDistinctClusterCases.NumericSeparatorClause,
            SqlNullDistinctClusterCases.NumericRadixSeparatorClause, SqlNullDistinctClusterCases.NumericOctalSeparatorClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, item.Command, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private const string OrderBy = " ORDER BY label ASC ";
}
