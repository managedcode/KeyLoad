using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using A = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryAssertions;
using P = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpAnalyticsDropDiscoveryRetirement
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers caller,
        McpAnalyticsDropDiscoveryState state, CancellationToken token)
    {
        var parent = Guid.NewGuid();
        var request = new DropTableRequest(state.Partition.TenantId, state.Partition.DatabaseId, P.Table);
        var original = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.DropTableAsync(parent, request, token));
        await PublicShapeAsync(request, parent, original);
        await SqlClusterProtocol.EqualAsync(state.Table, await SqlClusterProtocol.SdkAsync<ResourceDefinition>(caller.Sdk,
            new(state.Partition, P.CreateSql, CommandId: Guid.NewGuid()), token));
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(P.Table, P.FreshId, P.FreshJson, 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await A.ReceiptAsync(command, receipt, [new("putDocument", P.Table, P.FreshId, 1)]);
        var fresh = await A.CaptureAsync(caller, new(state.Partition, P.Table, P.FreshId), P.FreshJson, token);
        await Assert.That(fresh.Lifetime!.Incarnation).IsNotEqualTo(state.OldRow.Lifetime!.Incarnation);
        foreach (var route in Enum.GetValues<DropLifecycleClusterRoute>())
        {
            var replay = await DropLifecycleClusterCalls.InvokeAsync(caller, parent, request, state.Partition, route, token);
            await SqlClusterProtocol.EqualAsync(original, replay);
            await UnchangedAsync(caller, state, fresh, command, receipt, token);
        }
    }

    private static async Task UnchangedAsync(RequestCqrsClusterCallers caller, McpAnalyticsDropDiscoveryState state,
        EntityRef fresh, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await A.RowAsync(caller, fresh, P.FreshJson, token);
        var old = await caller.Sdk.GetAsync(state.OldRow, token);
        await Assert.That(old.IsFailed).IsTrue();
        await Assert.That(old.Value).IsNull();
        await Assert.That(old.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.OldRow), token), ErrorCode.OwnershipLost, dispatched: true);
        var name = state.OldRow with
        {
            Lifetime = null
        };
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.GetAsync(name, token)));
        await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await A.ReadsAsync(caller, state, token);
        await A.RowAsync(caller, fresh, P.FreshJson, token);
    }

    private static async Task PublicShapeAsync(DropTableRequest request, Guid parent, DropTableResult actual)
    {
        await Assert.That(actual.TenantId).IsEqualTo(request.TenantId);
        await Assert.That(actual.DatabaseId).IsEqualTo(request.DatabaseId);
        await Assert.That(actual.ResourceName).IsEqualTo(request.ResourceName);
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(parent);
        await Assert.That(actual.Receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(NativeCatalogRoute.PartitionId);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(0);
        await Assert.That(actual.Receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(actual.Receipt.Mutations.IsEmpty).IsTrue();
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.Receipt.PersistedCopies).IsEqualTo(3);
    }
}
