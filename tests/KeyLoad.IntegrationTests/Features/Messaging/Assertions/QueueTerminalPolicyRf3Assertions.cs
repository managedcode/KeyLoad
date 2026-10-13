using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Assertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DomainAsync(RequestCqrsRf3Callers worker, QueueTerminalPolicyRf3State state,
        CancellationToken token)
    {
        await MessageAsync(worker, state.Lane, QueueTerminalPolicyRf3Protocol.Message, state.Original, token);
        await MessageAsync(worker, state.Lane, QueueTerminalPolicyRf3Protocol.Healthy, state.Healthy, token);
        await DocumentAsync(worker, state, QueueTerminalPolicyRf3Protocol.Refused, null, token);
        DocumentResult? expected = state.Healthy is null ? null : new(new(state.Partition,
            QueueTerminalPolicyRf3Protocol.Collection, QueueTerminalPolicyRf3Protocol.Healthy), 1L,
            QueueTerminalPolicyRf3Protocol.Payload, false, []);
        await DocumentAsync(worker, state, QueueTerminalPolicyRf3Protocol.Healthy, expected, token);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await DomainAsync(worker, state, token);
        await ResourcesAsync(admin, state, token);
        await PlacementAsync(admin, state, token);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(state.Partition, token)), state.Baseline);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(await admin.Mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value, state.Baseline);
    }

    internal static async Task CaptureAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await DomainAsync(worker, state, token);
        await ResourcesAsync(admin, state, token);
        state.Baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Partition, token));
        await CurrentAsync(admin, worker, state, token);
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers worker, QueueLaneRef lane, string id,
        MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await worker.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await worker.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    private static async Task DocumentAsync(RequestCqrsRf3Callers worker, QueueTerminalPolicyRf3State state,
        string id, DocumentResult? expected, CancellationToken token)
    {
        var reference = new EntityRef(state.Partition, QueueTerminalPolicyRf3Protocol.Collection, id);
        await EqualAsync(await PublicReadAssertions.DocumentAsync(await worker.Sdk.GetAsync(reference, token)), expected);
        await EqualAsync((await PublicReadAssertions.DocumentAsync(await worker.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value, expected);
    }

    private static async Task ResourcesAsync(RequestCqrsRf3Callers admin, QueueTerminalPolicyRf3State state,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(state.Partition.TenantId, state.Partition.DatabaseId), token));
        AdminResourceInfo[] expected = [new(QueueTerminalPolicyRf3Protocol.Collection, ResourceKind.Collection,
            state.Partition.TransactionDomainId, 1L, 0, false), new(QueueTerminalPolicyRf3Protocol.Queue,
            ResourceKind.WorkQueue, state.Partition.TransactionDomainId, 1L, 0, false)];
        await EqualAsync(page.Items.OrderBy(row => row.Name, StringComparer.Ordinal).ToArray(), expected);
        await Assert.That(page.NextAfterName).IsNull();
    }

    private static async Task PlacementAsync(RequestCqrsRf3Callers admin, QueueTerminalPolicyRf3State state,
        CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, state.Partition), token)), state.Placement);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await admin.Mcp.CallAsync(
            McpCallerTools.AdminPartitionPlacementRead, new AtomicPartitionPlacementReadRequest(1, state.Partition), token))).Value, state.Placement);
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid command, QueueTerminalPolicyRf3State state,
        MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(receipt.Mutations.ToArray(), mutations);
    }
}
