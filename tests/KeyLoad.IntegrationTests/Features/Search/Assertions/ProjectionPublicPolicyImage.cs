using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class ProjectionPublicPolicyImage
{
    internal static async Task<QueryPage> ReadAsync(global::KeyLoad.Client.KeyLoadClient root, EventProjectionRf3Scenario scenario,
        CancellationToken token)
    {
        var request = new QueryRequest(scenario.Partition,
            ProjectionPublicPolicyProtocol.ModelSql, AllowFullScan: true);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.QueryAsync(request, token));
        QueryRow[] expected =
        [
            new(EventProjectionRf3Scenario.BaselineId, EventProjectionRf3Scenario.Revision,
                ProjectionPublicPolicyProtocol.BaselineJson, RedactedFields: []),
            new(EventProjectionRf3Scenario.TargetId, EventProjectionRf3Scenario.Revision,
                ProjectionPublicPolicyProtocol.DerivedJson, RedactedFields: []),
            new(EventProjectionRf3Scenario.SourceId, EventProjectionRf3Scenario.Revision,
                ProjectionPublicPolicyProtocol.SourceJson, RedactedFields: [])
        ];
        await Assert.That(global::KeyLoad.JsonDefaults.Serialize(actual.Rows.ToArray()).AsSpan()
            .SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.AccessPath).IsEqualTo(ProjectionPublicPolicyProtocol.Scan);
        return actual;
    }
}
