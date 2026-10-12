using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class QueueReferenceSelectSources
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers admin, QueueReferenceSelectState state, CancellationToken token)
    {
        foreach (var (id, json) in new[] { ("c1", QueueReferenceSelectProtocol.FirstJson), ("c2", QueueReferenceSelectProtocol.HiddenJson) })
        {
            var reference = new EntityRef(state.Partition, QueueReferenceSelectProtocol.Target, id);
            var actual = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(reference, token));
            await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, 1, json, false, []), actual);
        }
        var missing = new EntityRef(state.Partition, QueueReferenceSelectProtocol.Target, "m");
        var repaired = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(missing, token));
        await SqlRf3Protocol.EqualAsync(state.Repair is null ? null : new DocumentResult(missing, 1,
            QueueReferenceSelectProtocol.RepairedJson, false, []), repaired);
        var payloads = new[] { QueueReferenceSelectProtocol.Payload(state.Partition, "c1", 7),
            QueueReferenceSelectProtocol.Payload(state.Partition, "m", 7),
            QueueReferenceSelectProtocol.Payload(state.Partition, "c2", 7),
            QueueReferenceSelectProtocol.Payload(state.Partition, "c1", 2), "{\"score\":7}" };
        var ids = new[] { "a", "b", "c", "d", "e" };
        for (var index = 0; index < ids.Length; index++)
        {
            await MessageAsync(admin, state.Partition, QueueReferenceSelectProtocol.Queue, ids[index],
                payloads[index], index + 1L, token);
        }
        await MessageAsync(admin, state.Partition, QueueReferenceSelectProtocol.InvalidQueue, "wrong",
            QueueReferenceSelectProtocol.Payload(state.Partition with { PartitionKey = "foreign" }, "c1", 7), 1L, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Seed, token)));
        if (state.Repair is not null)
        {
            await SqlRf3Protocol.EqualAsync(state.RepairReceipt,
                await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Repair, token)));
        }
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers admin, PartitionRef partition,
        string queue, string id, string payload, long sequence, CancellationToken token)
    {
        var request = new InspectMessageRequest(new(partition, queue), id);
        var sdk = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(request, token));
        var official = await PublicReadAssertions.MessageAsync(await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token));
        await SqlRf3Protocol.EqualAsync(sdk, official.Value);
        if (sdk is null)
        { throw new InvalidOperationException("The original queue source is unavailable."); }
        await SqlRf3Protocol.EqualAsync(new MessageMetadata(id, MessageState.Ready, 0, 1, sequence, null, null), sdk.Metadata);
        await Assert.That(sdk.PayloadJson).IsEqualTo(payload);
        await Assert.That(sdk.HeadersJson).IsEqualTo("{}");
    }
}
