namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanOperandRf3Controls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score TRUE", "score =", "1", "'true'", "@absent", "NOT" })
        {
            await SqlBooleanTruthRf3Refusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, ErrorCode code, CancellationToken token)
    {
        var before = await SqlBooleanTruthRf3Image.ReadAsync(root, state, token);
        foreach (var item in SqlBooleanTruthRf3Cases.All.Where(item => item.Clause == SqlBooleanTruthRf3Cases.BareClause
            || item.Clause == SqlBooleanTruthRf3Cases.NegatedBareClause))
        {
            await SqlBooleanTruthRf3Refusals.RejectAsync(fixture, state, item.Clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
