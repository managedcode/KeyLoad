namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNumericSeparatorClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score = 1_",
            "score = 1__0",
            "score = .1_",
            "score = 1_.0",
            "score = 1._0",
            "score = 1e_1",
            "score = 1_e1",
            "score = 1e+_1",
            "score = 0x__1",
            "score = 0x1_",
            "score = 0b_",
            "score = 0o1__0",
            "score = + 0b_1",
            "score = 79_228_162_514_264_337_593_543_950_336",
            "score = 0x_1_0000_0000_0000_0000_0000_0000",
            "score = 0o_1_0000_0000_0000_0000_0000_0000_0000_0000" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var window in new[] { "LIMIT 0_0",
            "LIMIT 2_147_483_648",
            "OFFSET 2_147_483_648 LIMIT 1",
            "FETCH FIRST 2_147_483_648 ROWS ONLY",
            "LIMIT +0_1",
            "OFFSET -0_1 LIMIT 1",
            "FETCH FIRST 0x_1 ROW ONLY",
            "LIMIT 0b_1",
            "OFFSET 0o_1 LIMIT 1",
            "LIMIT 1_0e-1",
            "FETCH FIRST 1.0_0 ROW ONLY" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + window;
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, SqlNullDistinctClusterCases.NumericSeparatorClause,
                ErrorCode.Validation, token, healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var window in new[] { "LIMIT 0_1 OFFSET 0_0 LIMIT 1", "FETCH FIRST 0_1 ROW ONLY LIMIT 1" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + window;
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, SqlNullDistinctClusterCases.NumericSeparatorClause,
                ErrorCode.UnsupportedCapability, token, healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlNullDistinctClusterCases.NumericSeparatorClause,
            SqlNullDistinctClusterCases.NumericRadixSeparatorClause, SqlNullDistinctClusterCases.NumericOctalSeparatorClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private const string OrderBy = " ORDER BY label ASC ";
}
