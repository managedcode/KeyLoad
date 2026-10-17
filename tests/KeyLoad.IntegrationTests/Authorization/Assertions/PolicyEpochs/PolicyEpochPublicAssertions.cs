using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicAssertions
{
    internal static async Task<string[]> PagesAsync(RequestCqrsRf3Callers reader, KeyLoadClient root,
        ResourcePolicyUpdateRf3Scenario scenario, bool redacted, CancellationToken token)
    {
        var cursors = new string[PolicyEpochPublicProtocol.RouteCount];
        for (var route = 0; route < cursors.Length; route++)
        {
            var before = await ImageAsync(root, scenario, token);
            var request = PolicyEpochPublicProtocol.Request(scenario.Partition);
            var first = await PolicyEpochPublicCalls.ReadAsync(reader, request, route, token);
            await RowAsync(first, ResourcePolicyUpdateRf3Protocol.DocumentId,
                redacted ? PolicyEpochPublicProtocol.FirstSafeJson : ResourcePolicyUpdateRf3Protocol.SeedJson, redacted);
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

    internal static async Task<QueryPage> ImageAsync(KeyLoadClient root, ResourcePolicyUpdateRf3Scenario scenario,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await root.QueryAsync(
            PolicyEpochPublicProtocol.Complete(scenario.Partition), token));
        QueryRow[] expected = [new(ResourcePolicyUpdateRf3Protocol.DocumentId, scenario.DocumentRevision,
            ResourcePolicyUpdateRf3Protocol.SeedJson, RedactedFields: []),
            new(PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SeedRevision,
                PolicyEpochPublicProtocol.SecondJson, RedactedFields: [])];
        await SqlRf3Protocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        var resources = await McpCallerAssertions.SdkSuccessAsync(await root.ListResourcesAsync(
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId), token));
        var definition = resources.Items.Single(item => item.Name == ResourcePolicyUpdateRf3Protocol.Collection);
        var expectedDefinition = new AdminResourceInfo(ResourcePolicyUpdateRf3Protocol.Collection,
            scenario.InitialDefinition.Kind, scenario.InitialDefinition.TransactionDomainId,
            ResourcePolicyUpdateRf3Protocol.CurrentSchemaVersion, scenario.InitialDefinition.Indexes.Length,
            scenario.InitialDefinition.Paused);
        await SqlRf3Protocol.EqualAsync(expectedDefinition, definition);
        return page;
    }

    private static async Task RowAsync(QueryPage page, string id, string json, bool redacted)
    {
        QueryRow[] expected = [new(id, PolicyEpochPublicProtocol.SeedRevision, json, redacted,
            redacted ? [ResourcePolicyUpdateRf3Protocol.SecretPath] : [])];
        await SqlRf3Protocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(0L);
    }
}
