using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CanonicalApplyNetworkAssertions
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers caller, CommandRequest command,
        CommitReceipt original, CancellationToken token)
    {
        await Assert.That(original.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(original.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(original.Token.Position).IsGreaterThan(0);
        await Assert.That(original.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(original.Mutations.Length).IsEqualTo(2);
        await Assert.That(original.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId, 2));
        await Assert.That(original.Mutations[1]).IsEqualTo(new MutationReceipt("enqueue", CanonicalApplyNetworkData.Queue,
            CanonicalApplyNetworkData.Message, 1));
        var before = await ReadAsync(caller, command.Partition, token);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await EqualAsync(sdk, original);
        await EqualAsync(mcp.Value, original);
        await EqualReadAsync(await ReadAsync(caller, command.Partition, token), before);
        var changed = command with { Mutations = [new EnqueueMessage(CanonicalApplyNetworkData.Queue, "wrong", "{}", "{}")] };
        var denied = await caller.Sdk.CommitAsync(changed, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem!.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await EqualReadAsync(await ReadAsync(caller, command.Partition, token), before);
        var healthy = new CommandRequest(Guid.NewGuid(), command.Partition,
            [new EnqueueMessage(CanonicalApplyNetworkData.Queue, CanonicalApplyNetworkData.Healthy,
                CanonicalApplyNetworkData.Payload, CanonicalApplyNetworkData.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(healthy, token));
        await Assert.That(receipt.CommandId).IsEqualTo(healthy.CommandId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(original.Token.Position);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("enqueue", CanonicalApplyNetworkData.Queue,
            CanonicalApplyNetworkData.Healthy, 1));
        await MessageAsync(caller, new(command.Partition, CanonicalApplyNetworkData.Queue), CanonicalApplyNetworkData.Healthy, 2, token);
    }
    private static async Task<(DocumentResult Document, MessageInspection Message)> ReadAsync(RequestCqrsRf3Callers caller,
        PartitionRef partition, CancellationToken token)
    {
        var reference = new EntityRef(partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        var document = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(reference, token));
        await Assert.That(document!.Reference).IsEqualTo(reference);
        await Assert.That(document.Json).IsEqualTo(RequestCqrsRf3Protocol.ChangedDocumentJson);
        await Assert.That(document.Revision).IsEqualTo(2L);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields.IsEmpty).IsTrue();
        var official = await McpCallerAssertions.SuccessAsync<DocumentResult>(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
        await EqualAsync(official.Value, document);
        var message = await MessageAsync(caller, new(partition, CanonicalApplyNetworkData.Queue), CanonicalApplyNetworkData.Message, 1, token);
        return (document, message);
    }
    private static async Task<MessageInspection> MessageAsync(RequestCqrsRf3Callers caller, QueueLaneRef lane,
        string id, long sequence, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.InspectAsync(new(lane, id), token));
        var official = await McpCallerAssertions.SuccessAsync<MessageInspection>(await caller.Mcp.CallAsync(McpCallerTools.MessagesInspect, new InspectMessageRequest(lane, id), token));
        var expected = new MessageInspection(new(id, MessageState.Ready, 0, 1, sequence, null, null, null, 0, null, 1, null),
            CanonicalApplyNetworkData.Payload, CanonicalApplyNetworkData.Headers);
        await EqualAsync(sdk, expected);
        await EqualAsync(official.Value, expected);
        return sdk!;
    }
    private static async Task EqualReadAsync((DocumentResult Document, MessageInspection Message) actual,
        (DocumentResult Document, MessageInspection Message) expected)
    {
        await EqualAsync(actual.Document, expected.Document);
        await EqualAsync(actual.Message, expected.Message);
    }
    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
