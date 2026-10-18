namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "U&\"score\\D800\" = 1",
            "U&\"score\\DC00\" = 1",
            "U&\"score\\0000\" = 1",
            "U&\"score\\+110000\" = 1",
            "U&\"score\\zzzz\" = 1",
            "U&\"score",
            "U&\"score\" UESCAPE '0' = 1",
            "U&\"score\" UESCAPE '!!' = 1",
            "U&\"score\" UESCAPE '\u00e9' = 1" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var window in new[] { "LIMIT U&\"1\"", "OFFSET U&\"1\" LIMIT 1", "FETCH FIRST U&\"1\" ROW ONLY" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + SqlUnicodeIdentifierCases.Clause + OrderBy + window;
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, SqlUnicodeIdentifierCases.Clause,
                ErrorCode.Validation, token, healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }

    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlUnicodeIdentifierCases.Clause,
            SqlUnicodeIdentifierCases.WideClause, SqlUnicodeIdentifierCases.CustomClause })
        {
            var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static bool IsSelected(SqlNullDistinctClusterCase item)
        => item.Clause is SqlUnicodeIdentifierCases.Clause or SqlUnicodeIdentifierCases.WideClause or SqlUnicodeIdentifierCases.CustomClause;

    private const string OrderBy = " ORDER BY label ASC ";
}
