using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateQueues
{
    internal static async Task InitializeAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        await Assert.That(ctx.ResourceCount).IsEqualTo(3);
        for (var index = 0; index < ctx.ResourceCount; index++)
        {
            var queue = Queue(index);
            var definition = new ResourceDefinition(queue, ResourceKind.WorkQueue, ctx.Partition.TransactionDomainId)
            {
                QueuePolicy = new QueuePolicy { MaxAttempts = 1 }
            };
            await EqualAsync(definition, await ctx.SdkAsync(ctx.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(ctx.Partition.TenantId, ctx.Partition.DatabaseId, definition), token)));
            var topic = new ResourceDefinition(Topic(index), ResourceKind.Topic, ctx.Partition.TransactionDomainId);
            await EqualAsync(topic, await ctx.SdkAsync(ctx.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(ctx.Partition.TenantId, ctx.Partition.DatabaseId, topic), token)));
            await BaselineAsync(ctx, index, token);
            await FunctionalGateTopicExercise.InitializeAsync(ctx, index, token);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, int iteration, int resourceIndex, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iteration);
        ArgumentOutOfRangeException.ThrowIfNegative(resourceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(resourceIndex, ctx.ResourceCount);
        await FunctionalGateQueueExercise.RunAsync(ctx, iteration, resourceIndex, token);
        await FunctionalGateTopicExercise.RunAsync(ctx, iteration, resourceIndex, token);
    }

    internal static async Task VerifyBaselineAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        for (var index = 0; index < ctx.ResourceCount; index++)
        {
            var request = new InspectMessageRequest(new(ctx.Partition, Queue(index)), "gate_baseline");
            var expected = new MessageInspectionResult(new(new("gate_baseline", MessageState.Acked, 1, 3,
                1, null, null, LeaseVersion: 1), null, null));
            await EqualAsync(expected, await ctx.SdkAsync(ctx.Administrator.Sdk.InspectAsync(request, token)));
            await EqualAsync(expected, await ctx.McpAsync<MessageInspectionResult>(ctx.Administrator,
                McpCallerTools.MessagesInspect, request, token));
            await FunctionalGateTopicExercise.VerifyBaselineAsync(ctx, index, token);
        }
    }

    internal static string Queue(int index) => "gate_queue_" + index.ToString(CultureInfo.InvariantCulture);
    internal static string Topic(int index) => "gate_topic_" + index.ToString(CultureInfo.InvariantCulture);
    internal static string Id(int iteration) => "gate_message_" + iteration.ToString(CultureInfo.InvariantCulture);
    internal static string Payload(FunctionalGateContext ctx, int iteration)
        => FunctionalGateBoundJson.Serialize(new
        {
            attributesJson = "{\"iteration\":" + iteration.ToString(CultureInfo.InvariantCulture) + "}",
            from = ctx.Bind(ctx.Anchor),
            label = "gate_work",
            to = ctx.Bind(ctx.Anchor)
        });

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected, JsonDefaults.Options),
            JsonSerializer.SerializeToNode(actual, JsonDefaults.Options))).IsTrue();

    internal static async Task ReceiptAsync(FunctionalGateContext ctx, CommitReceipt receipt,
        Guid commandId, MutationReceipt expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await EqualAsync(expected, await Assert.That(receipt.Mutations).HasSingleItem());
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(ctx.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(ctx.ReplicaCount);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
    }

    private static async Task BaselineAsync(FunctionalGateContext ctx, int index, CancellationToken token)
    {
        var lane = new QueueLaneRef(ctx.Partition, Queue(index));
        var payload = Payload(ctx, -1);
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
            [new EnqueueMessage(lane.Queue, "gate_baseline", payload)]);
        var receipt = await ctx.SdkAsync(ctx.Administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(ctx, receipt, command.CommandId, new("enqueue", lane.Queue, "gate_baseline", 1));
        var claim = new ReceiveRequest(Guid.NewGuid(), lane);
        var received = await ctx.McpAsync<ReceiveResult>(ctx.Administrator, McpCallerTools.MessagesReceive, claim, token);
        await Assert.That(received.RequestId).IsEqualTo(claim.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("gate_baseline");
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        await ReceiptAsync(ctx, await ctx.SdkAsync(ctx.Administrator.Sdk.CompleteAsync(ack, token)),
            ack.CommandId, new("Ack", lane.Queue, delivery.Id, 3));
    }
}
