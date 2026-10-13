namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeStringRf3Controls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { "label = U&'\\123'",
            "label = U&'\\+110000'",
            "label = U&'\\D800'",
            "label = U&'\\DC00'",
            "label = U&'\\D800x\\DC00'",
            "label = U&'\\0000'",
            "label = U&'\\+000000'",
            "label = U&'\\g000'",
            "label = U&'unterminated",
            "label = U&'low' UESCAPE",
            "label = U&'low' UESCAPE ''",
            "label = U&'low' UESCAPE '!!'",
            "label = U&'low' UESCAPE '+'",
            "label = U&'low' UESCAPE 'a'",
            "label = U&'low' UESCAPE ' '",
            "label = U&'low' UESCAPE 'é'" })
        {
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root,
        ErrorCode code, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { SqlNullDistinctRf3Cases.UnicodeClause, SqlNullDistinctRf3Cases.UnicodeCustomClause })
        {
            var item = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == clause);
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

}
