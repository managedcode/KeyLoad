namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierRf3Controls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
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
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var window in new[] { "LIMIT U&\"1\"", "OFFSET U&\"1\" LIMIT 1", "FETCH FIRST U&\"1\" ROW ONLY" })
        {
            var sql = SqlNullDistinctRf3Cases.Prefix + SqlUnicodeIdentifierCases.Clause + OrderBy + window;
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, SqlUnicodeIdentifierCases.Clause,
                ErrorCode.Validation, token, healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }

    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlUnicodeIdentifierCases.Clause,
            SqlUnicodeIdentifierCases.WideClause, SqlUnicodeIdentifierCases.CustomClause })
        {
            var item = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static bool IsSelected(SqlNullDistinctRf3Case item)
        => item.Clause is SqlUnicodeIdentifierCases.Clause or SqlUnicodeIdentifierCases.WideClause or SqlUnicodeIdentifierCases.CustomClause;

    private const string OrderBy = " ORDER BY label ASC ";
}
