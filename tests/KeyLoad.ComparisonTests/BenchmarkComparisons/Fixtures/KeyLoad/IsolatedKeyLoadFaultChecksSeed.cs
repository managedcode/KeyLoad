using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Private retained ACKs and credentials are never serialized into native evidence.</summary>
internal sealed record IsolatedKeyLoadFaultChecksSeed(PartitionRef Partition, CommandRequest SentinelCommand,
    CommitReceipt SentinelReceipt, DeliveryCommand Ack, CommitReceipt AckReceipt,
    IsolatedKeyLoadPublicChecksIdentity Reader, int RequiredCopies)
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

    internal static async Task<IsolatedKeyLoadFaultChecksSeed> CreateAsync(KeyLoadClient sdk, int requiredCopies, CancellationToken token)
    {
        var partition = new PartitionRef("isolated-fault-" + Guid.NewGuid().ToString("N"), "fault-checks", "fault-domain",
            Guid.NewGuid().ToString("N"));
        await ConfigureAsync(sdk, partition, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, "sentinel", Payload, 0), new EnqueueMessage(Queue, Message, Payload)]);
        var receipt = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
            attempt => sdk.CommitAsync(command, attempt), token));
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(receipt, command.CommandId, 2, partition, requiredCopies);
        var received = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.ReceiveAsync(new(Guid.NewGuid(), new(partition, Queue)), attempt), token));
        await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(received.Deliveries[0].Id).IsEqualTo(Message);
        var ack = new DeliveryCommand(Guid.NewGuid(), new(partition, Queue), received.Deliveries[0].Token, DeliveryAction.Ack);
        var acknowledged = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
            attempt => sdk.CompleteAsync(ack, attempt), token));
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(acknowledged, ack.CommandId, 1, partition, requiredCopies);
        var reader = await IsolatedKeyLoadFaultChecksCalls.RunAsync(
            attempt => IsolatedKeyLoadPublicChecksIdentity.CreateAsync(sdk, partition, Collection, Capability.DocumentsRead, attempt), token);
        return new(partition, command, receipt, ack, acknowledged, reader, requiredCopies);
    }

    private static async Task ConfigureAsync(KeyLoadClient sdk, PartitionRef partition, CancellationToken token)
    {
        foreach (var (name, kind) in new[] { (Collection, ResourceKind.Collection), (Queue, ResourceKind.WorkQueue) })
        {
            var expected = new ResourceDefinition(name, kind, partition.TransactionDomainId);
            var configured = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, expected), attempt), token));
            await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(expected, configured);
        }
    }
}
