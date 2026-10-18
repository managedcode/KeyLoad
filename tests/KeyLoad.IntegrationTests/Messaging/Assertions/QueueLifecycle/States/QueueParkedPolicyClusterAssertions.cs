using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyClusterAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DomainAsync(RequestCqrsClusterCallers worker, QueueParkedPolicyClusterState state,
        CancellationToken token)
    {
        foreach (var row in state.Messages)
        {
            await MessageAsync(worker, state.Lane, row.Key, row.Value, token);
        }
        await DocumentAsync(worker, state, QueueParkedPolicyClusterProtocol.Missing, null, token);
    }

    internal static async Task CurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        await DomainAsync(worker, state, token);
        await ResourcesAsync(admin, state, token);
        await PlacementAsync();
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(state.Partition, token)), state.Baseline);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(await admin.Mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value, state.Baseline);
    }

    internal static async Task CaptureAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        await DomainAsync(worker, state, token);
        await ResourcesAsync(admin, state, token);
        state.Baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Partition, token));
        await CurrentAsync(admin, worker, state, token);
    }

    private static async Task MessageAsync(RequestCqrsClusterCallers worker, QueueLaneRef lane, string id,
        MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await worker.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    private static async Task DocumentAsync(RequestCqrsClusterCallers worker, QueueParkedPolicyClusterState state,
        string id, DocumentResult? expected, CancellationToken token)
    {
        var reference = new EntityRef(state.Partition, QueueParkedPolicyClusterProtocol.Collection, id);
        await EqualAsync(await PublicReadAssertions.DocumentAsync(await worker.Sdk.GetAsync(reference, token)), expected);
        await EqualAsync((await PublicReadAssertions.DocumentAsync(await worker.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value, expected);
    }

    private static async Task ResourcesAsync(RequestCqrsClusterCallers admin, QueueParkedPolicyClusterState state,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(state.Partition.TenantId, state.Partition.DatabaseId), token));
        AdminResourceInfo[] expected = [new(QueueParkedPolicyClusterProtocol.Collection, ResourceKind.Collection,
            state.Partition.TransactionDomainId, 1L, 0, false), new(QueueParkedPolicyClusterProtocol.Queue,
            ResourceKind.WorkQueue, state.Partition.TransactionDomainId, state.Definition.SchemaVersion, 0, false)];
        await EqualAsync(page.Items.OrderBy(row => row.Name, StringComparer.Ordinal).ToArray(), expected);
        await Assert.That(page.NextAfterName).IsNull();
    }

    private static async Task PlacementAsync()
    {
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid command, QueueParkedPolicyClusterState state,
        MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await EqualAsync(receipt.Mutations.ToArray(), mutations);
    }
}
