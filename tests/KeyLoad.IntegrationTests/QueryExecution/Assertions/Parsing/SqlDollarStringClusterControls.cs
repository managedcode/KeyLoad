namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlDollarStringClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "label = $tag$unfinished", "label = $TAG$low$tag$",
            "label = $tag-low$low$tag-low$" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token,
                healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var clause in new[] { "label = $$lo$$\n$$w$$", "label = $a$low$a$ $a$other$a$" })
        {
            await SqlNullDistinctClusterRefusals.RejectAsync(fixture, state, clause, ErrorCode.UnsupportedCapability,
                token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
