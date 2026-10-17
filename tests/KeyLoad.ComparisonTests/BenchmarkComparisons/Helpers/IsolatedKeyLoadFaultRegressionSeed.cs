using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Private retained ACKs and credentials are never serialized into native evidence.</summary>
internal sealed record IsolatedKeyLoadFaultRegressionSeed(PartitionRef Partition, CommandRequest SentinelCommand,
    CommitReceipt SentinelReceipt, DeliveryCommand Ack, CommitReceipt AckReceipt,
    IsolatedKeyLoadPublicRegressionIdentity Reader)
{
    internal const string Payload = "{\"value\":\"isolated-fault-sentinel\"}";
    private const string Collection = "faultdocs";
    private const string Queue = "faultqueue";
    private const string Message = "fault-message";
    internal EntityRef Sentinel => new(Partition, Collection, "sentinel");
    internal QueueLaneRef Lane => new(Partition, Queue);
    internal InspectMessageRequest Inspect => new(Lane, Message);
    internal EntityRef PhaseDocument(int phase) => new(Partition, Collection, "phase-" + phase.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal CommandRequest Create(EntityRef reference) => new(Guid.NewGuid(), Partition,
        [new PutDocument(reference.Collection, reference.Id, Payload, 0)]);

    internal static async Task<IsolatedKeyLoadFaultRegressionSeed> CreateAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var partition = new PartitionRef("isolated-fault-" + Guid.NewGuid().ToString("N"), "fault-regression", "fault-domain",
            Guid.NewGuid().ToString("N"));
        await ConfigureAsync(sdk, partition, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, "sentinel", Payload, 0), new EnqueueMessage(Queue, Message, Payload)]);
        var receipt = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
            attempt => sdk.CommitAsync(command, attempt), token));
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, command.CommandId, 2, partition);
        var received = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.ReceiveAsync(new(Guid.NewGuid(), new(partition, Queue)), attempt), token));
        await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(received.Deliveries[0].Id).IsEqualTo(Message);
        var ack = new DeliveryCommand(Guid.NewGuid(), new(partition, Queue), received.Deliveries[0].Token, DeliveryAction.Ack);
        var acknowledged = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
            attempt => sdk.CompleteAsync(ack, attempt), token));
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(acknowledged, ack.CommandId, 1, partition);
        var reader = await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
            attempt => IsolatedKeyLoadPublicRegressionIdentity.CreateAsync(sdk, partition, Collection, Capability.DocumentsRead, attempt), token);
        return new(partition, command, receipt, ack, acknowledged, reader);
    }

    private static async Task ConfigureAsync(KeyLoadClient sdk, PartitionRef partition, CancellationToken token)
    {
        foreach (var (name, kind) in new[] { (Collection, ResourceKind.Collection), (Queue, ResourceKind.WorkQueue) })
        {
            var expected = new ResourceDefinition(name, kind, partition.TransactionDomainId);
            var configured = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, expected), attempt), token));
            await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(expected, configured);
        }
    }
}
