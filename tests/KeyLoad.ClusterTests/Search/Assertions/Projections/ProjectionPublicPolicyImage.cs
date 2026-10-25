using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Search;

internal static class ProjectionPublicPolicyImage
{
    internal static async Task<QueryPage> ReadAsync(global::KeyLoad.Client.KeyLoadClient root, EventProjectionClusterScenario scenario,
        CancellationToken token)
    {
        var request = new QueryRequest(scenario.Partition,
            ProjectionPublicPolicyProtocol.ModelSql, AllowFullScan: true);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.QueryAsync(request, token));
        QueryRow[] expected =
        [
            new(EventProjectionClusterScenario.BaselineId, EventProjectionClusterScenario.Revision,
                ProjectionPublicPolicyProtocol.BaselineJson, RedactedFields: []),
            new(EventProjectionClusterScenario.TargetId, EventProjectionClusterScenario.Revision,
                ProjectionPublicPolicyProtocol.DerivedJson, RedactedFields: []),
            new(EventProjectionClusterScenario.SourceId, EventProjectionClusterScenario.Revision,
                ProjectionPublicPolicyProtocol.SourceJson, RedactedFields: [])
        ];
        await Assert.That(global::KeyLoad.JsonDefaults.Serialize(actual.Rows.ToArray()).AsSpan()
            .SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.AccessPath).IsEqualTo(ProjectionPublicPolicyProtocol.Scan);
        return actual;
    }
}
