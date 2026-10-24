namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlRadixIntegerClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score = 0b",
            "score = 0B2",
            "score = 0o",
            "score = 0O8",
            "score = 0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
            "score = -0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
            "score = 0o100000000000000000000000000000000",
            "score = -0o100000000000000000000000000000000" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var clause in new[] { "score = 0b1.0",
            "score = 0o1.0",
            "score = 0b1_0.0",
            "score = 0o1_0.0",
            "score = 0b102",
            "score = 0o128" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.UnsupportedCapability,
                token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var window in new[] { "LIMIT 0b1",
            "LIMIT 0o1",
            "OFFSET 0b1 LIMIT 1",
            "OFFSET 0o1 LIMIT 1",
            "FETCH FIRST 0b1 ROW ONLY",
            "FETCH FIRST 0o1 ROW ONLY",
            "LIMIT +0b1",
            "OFFSET -0o1 LIMIT 1",
            "FETCH FIRST +0o1 ROW ONLY" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause + OrderBy + window;
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, SqlNullDistinctClusterCases.PositiveClause,
                ErrorCode.Validation, token, healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlNullDistinctClusterCases.BinaryClause, SqlNullDistinctClusterCases.OctalClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private const string OrderBy = " ORDER BY label ASC ";
}
