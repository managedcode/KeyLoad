namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlDollarStringRf3Controls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { "label = $tag$unfinished", "label = $TAG$low$tag$",
            "label = $tag-low$low$tag-low$" })
        {
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, ErrorCode.Validation, token,
                healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var clause in new[] { "label = $$lo$$\n$$w$$", "label = $a$low$a$ $a$other$a$" })
        {
            await SqlNullDistinctRf3Refusals.RejectAsync(fixture, state, clause, ErrorCode.UnsupportedCapability,
                token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }
}
