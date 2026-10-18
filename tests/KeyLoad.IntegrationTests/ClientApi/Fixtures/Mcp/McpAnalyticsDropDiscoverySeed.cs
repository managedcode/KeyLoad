using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using A = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryAssertions;
using P = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpAnalyticsDropDiscoverySeed
{
    internal static async Task<McpAnalyticsDropDiscoveryState> CreateAsync(RequestCqrsClusterCallers caller, CancellationToken token)
    {
        var partition = new PartitionRef("mcp-model-" + Guid.NewGuid().ToString("N"), "agents", "knowledge",
            Guid.NewGuid().ToString("N"));
        var analytics = new ResourceDefinition(P.Store, ResourceKind.AnalyticsStore, partition.TransactionDomainId)
        {
            AnalyticsSchema = new([P.Kind], [P.Value])
        };
        var anchorDefinition = new ResourceDefinition(P.Anchor, ResourceKind.Collection, partition.TransactionDomainId);
        foreach (var definition in new[] { analytics, anchorDefinition })
        {
            await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(partition.TenantId, partition.DatabaseId, definition), token)));
        }

        var table = new ResourceDefinition(P.Table, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(P.Id, [new(P.Id, RelationalColumnType.Text), new(P.Value, RelationalColumnType.WholeNumber)])
        };
        await SqlClusterProtocol.EqualAsync(table, await SqlClusterProtocol.SdkAsync<ResourceDefinition>(caller.Sdk,
            new(partition, P.CreateSql, CommandId: Guid.NewGuid()), token));
        var anchor = await SeedAnchorAsync(caller, partition, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(P.Table, P.OldId, P.OldJson, 0),
                new UpsertAnalyticsEvents(P.Store, [P.Event(P.First, 2, anchor), P.Event(P.Second, 6, anchor)],
                    ImmutableDictionary<string, long>.Empty.Add(P.First, 0).Add(P.Second, 0))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await A.ReceiptAsync(command, receipt,
            [new("putDocument", P.Table, P.OldId, 1), new(AnalyticsStoreProtocol.UpsertKind, P.Store, P.Second, 1)]);
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        var old = await A.CaptureAsync(caller, new(partition, P.Table, P.OldId), P.OldJson, token);
        return new(partition, table, anchor, old);
    }

    private static async Task<EntityRef> SeedAnchorAsync(RequestCqrsClusterCallers caller, PartitionRef partition, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(P.Anchor, P.AnchorId, P.AnchorJson, 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await A.ReceiptAsync(command, receipt, [new("putDocument", P.Anchor, P.AnchorId, 1)]);
        return await A.CaptureAsync(caller, new(partition, P.Anchor, P.AnchorId), P.AnchorJson, token);
    }
}
