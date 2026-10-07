using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneReceiveRf3Flow
{
    private const string ArgumentsParameter = "args";
    internal const string Tool = "keyload_messages_receive_across_lanes";
    internal const string Message = "input";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"job\"}";

    internal static async Task<QueueLaneRef[]> SeedAsync(KeyLoadClient administrator, CancellationToken token)
    {
        var tenant = "multi-receive-" + Guid.NewGuid().ToString("N");
        var lanes = new QueueLaneRef[3];
        for (var index = 0; index < lanes.Length; index++)
        {
            var partition = new PartitionRef(tenant, "database", "work", "partition-" + index);
            var queue = "queue-" + index;
            lanes[index] = new(partition, queue);
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, "database", new ResourceDefinition(queue, ResourceKind.WorkQueue, "work")), token));
            await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), partition,
                [new EnqueueMessage(queue, Message, Payload, Headers)]), token));
        }
        return lanes;
    }

    internal static SqlOperationRequest Sql(MultiLaneReceiveRequest request)
        => new(request.Requests[0].Lane.Partition, "CALL keyload_messages_receive_across_lanes(@args)",
            new(StringComparer.Ordinal)
            {
                [ArgumentsParameter] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(request), JsonDefaults.Options)
            });

    internal static async Task EquivalentAsync(MultiLaneReceiveResult actual, MultiLaneReceiveResult expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task CommittedAsync(KeyLoadClient administrator, QueueLaneReceiveOutcome outcome,
        ReceiveRequest request, CancellationToken token)
    {
        await Assert.That(outcome.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(outcome.Lane).IsEqualTo(request.Lane);
        await Assert.That(outcome.Status).IsEqualTo(QueueLaneReceiveStatus.Committed);
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(outcome.SafeDetail).IsNull();
        var result = outcome.Result ?? throw new InvalidOperationException("Committed lane omitted its durable result.");
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(Message);
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var inspected = await McpCallerAssertions.SdkSuccessAsync(await administrator.InspectAsync(new(request.Lane, Message), token));
        await Assert.That(inspected!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(inspected.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
        await Assert.That(inspected.Metadata.LeaseVersion).IsEqualTo(delivery.LeaseVersion);
        await Assert.That(inspected.Metadata.Attempts).IsEqualTo(delivery.Attempt);
        await Assert.That(inspected.PayloadJson).IsEqualTo(Payload);
        await Assert.That(inspected.HeadersJson).IsEqualTo(Headers);
    }
}
