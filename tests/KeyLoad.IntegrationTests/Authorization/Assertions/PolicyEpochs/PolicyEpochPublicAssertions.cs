using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicAssertions
{
    internal static async Task<string[]> PagesAsync(RequestCqrsClusterCallers reader, KeyLoadClient root,
        ResourcePolicyUpdateClusterScenario scenario, bool redacted, CancellationToken token)
    {
        var cursors = new string[PolicyEpochPublicProtocol.RouteCount];
        for (var route = 0; route < cursors.Length; route++)
        {
            var before = await ImageAsync(root, scenario, token);
            var request = PolicyEpochPublicProtocol.Request(scenario.Partition);
            var first = await PolicyEpochPublicCalls.ReadAsync(reader, request, route, token);
            await RowAsync(first, ResourcePolicyUpdateClusterProtocol.DocumentId,
                redacted ? PolicyEpochPublicProtocol.FirstSafeJson : ResourcePolicyUpdateClusterProtocol.SeedJson, redacted);
            await ImageAsync(root, scenario, token);
            await Assert.That(first.Cursor).IsNotNull();
            cursors[route] = first.Cursor!;
            var second = await PolicyEpochPublicCalls.ReadAsync(reader, request with
            {
                Cursor = first.Cursor
            }, route, token);
            await RowAsync(second, PolicyEpochPublicProtocol.SecondId,
                redacted ? PolicyEpochPublicProtocol.SecondSafeJson : PolicyEpochPublicProtocol.SecondJson, redacted);
            await Assert.That(second.Cursor).IsNull();
            await Assert.That(second.CutPosition).IsEqualTo(first.CutPosition);
            var after = await ImageAsync(root, scenario, token);
            await Assert.That(first.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
            await Assert.That(first.CutPosition).IsLessThanOrEqualTo(after.CutPosition);
        }
        return cursors;
    }

    internal static async Task<QueryPage> ImageAsync(KeyLoadClient root, ResourcePolicyUpdateClusterScenario scenario,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await root.QueryAsync(
            PolicyEpochPublicProtocol.Complete(scenario.Partition), token));
        QueryRow[] expected = [new(ResourcePolicyUpdateClusterProtocol.DocumentId, scenario.DocumentRevision,
            ResourcePolicyUpdateClusterProtocol.SeedJson, RedactedFields: []),
            new(PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SeedRevision,
                PolicyEpochPublicProtocol.SecondJson, RedactedFields: [])];
        await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        var resources = await McpCallerAssertions.SdkSuccessAsync(await root.ListResourcesAsync(
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId), token));
        var definition = resources.Items.Single(item => item.Name == ResourcePolicyUpdateClusterProtocol.Collection);
        var expectedDefinition = new AdminResourceInfo(ResourcePolicyUpdateClusterProtocol.Collection,
            scenario.InitialDefinition.Kind, scenario.InitialDefinition.TransactionDomainId,
            ResourcePolicyUpdateClusterProtocol.CurrentSchemaVersion, scenario.InitialDefinition.Indexes.Length,
            scenario.InitialDefinition.Paused);
        await SqlClusterProtocol.EqualAsync(expectedDefinition, definition);
        return page;
    }

    private static async Task RowAsync(QueryPage page, string id, string json, bool redacted)
    {
        QueryRow[] expected = [new(id, PolicyEpochPublicProtocol.SeedRevision, json, redacted,
            redacted ? [ResourcePolicyUpdateClusterProtocol.SecretPath] : [])];
        await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(0L);
    }
}
