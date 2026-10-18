using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class QueueReferenceSelectSources
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers admin, QueueReferenceSelectState state, CancellationToken token)
    {
        foreach (var (id, json) in new[] { ("c1", QueueReferenceSelectProtocol.FirstJson), ("c2", QueueReferenceSelectProtocol.HiddenJson) })
        {
            var reference = new EntityRef(state.Partition, QueueReferenceSelectProtocol.Target, id);
            var actual = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(reference, token));
            await SqlClusterProtocol.EqualAsync(new DocumentResult(reference, 1, json, false, []), actual);
        }
        var missing = new EntityRef(state.Partition, QueueReferenceSelectProtocol.Target, "m");
        var repaired = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(missing, token));
        await SqlClusterProtocol.EqualAsync(state.Repair is null ? null : new DocumentResult(missing, 1,
            QueueReferenceSelectProtocol.RepairedJson, false, []), repaired);
        var payloads = new[] { ExpectedPayload(state.Partition, "c1", 7),
            ExpectedPayload(state.Partition, "m", 7),
            ExpectedPayload(state.Partition, "c2", 7),
            ExpectedPayload(state.Partition, "c1", 2), "{\"score\":7}" };
        var ids = new[] { "a", "b", "c", "d", "e" };
        for (var index = 0; index < ids.Length; index++)
        {
            await MessageAsync(admin, state.Partition, QueueReferenceSelectProtocol.Queue, ids[index],
                payloads[index], index + 1L, token);
        }
        await MessageAsync(admin, state.Partition, QueueReferenceSelectProtocol.InvalidQueue, "wrong",
            ExpectedPayload(state.Partition with
            {
                PartitionKey = "foreign"
            }, "c1", 7), 1L, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Seed, token)));
        if (state.Repair is not null)
        {
            await SqlClusterProtocol.EqualAsync(state.RepairReceipt,
                await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Repair, token)));
        }
    }

    private static string ExpectedPayload(PartitionRef partition, string id, int score)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            score,
            to = new
            {
                collection = QueueReferenceSelectProtocol.Target,
                id,
                partition = new
                {
                    atomicPartitionId = partition.AtomicPartitionId,
                    databaseId = partition.DatabaseId,
                    partitionKey = partition.PartitionKey,
                    tenantId = partition.TenantId,
                    transactionDomainId = partition.TransactionDomainId
                }
            }
        }, JsonDefaults.Options);

    private static async Task MessageAsync(RequestCqrsClusterCallers admin, PartitionRef partition,
        string queue, string id, string payload, long sequence, CancellationToken token)
    {
        var request = new InspectMessageRequest(new(partition, queue), id);
        var sdk = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(request, token));
        var official = await PublicReadAssertions.MessageAsync(await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token));
        await SqlClusterProtocol.EqualAsync(sdk, official.Value);
        if (sdk is null)
        {
            throw new InvalidOperationException("The original queue source is unavailable.");
        }
        await SqlClusterProtocol.EqualAsync(new MessageMetadata(id, MessageState.Ready, 0, 1, sequence, null, null), sdk.Metadata);
        await Assert.That(sdk.PayloadJson).IsEqualTo(payload);
        await Assert.That(sdk.HeadersJson).IsEqualTo("{}");
    }
}
