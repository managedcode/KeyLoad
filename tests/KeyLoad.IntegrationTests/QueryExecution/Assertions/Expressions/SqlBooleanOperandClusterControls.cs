namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanOperandClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score TRUE", "score =", "1", "'true'", "@absent", "NOT" })
        {
            await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    internal static async Task RequireAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root, ErrorCode code, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        foreach (var item in SqlBooleanTruthClusterCases.All.Where(item => item.Clause == SqlBooleanTruthClusterCases.BareClause
            || item.Clause == SqlBooleanTruthClusterCases.NegatedBareClause))
        {
            await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, state, item.Clause, code, token, ast: true, astCase: item);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
