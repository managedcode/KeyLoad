using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferPostAwaitSetup(RemoteTransferColdSeed Seed, MessagingRf3Identity Technical)
{
    internal static async Task<RemoteTransferPostAwaitSetup> CreateAsync(PartitionMovementBlobWireCallers administrator,
        PartitionMovementBlobWireSeed parent, CancellationToken token)
    {
        var target = parent.Partition;
        var source = new PartitionRef(target.TenantId, target.DatabaseId, target.TransactionDomainId,
            Guid.NewGuid().ToString(RemoteTransferColdProtocol.TransferIdFormat));
        var scenario = new MessagingRf3Scenario(source, target);
        await ConfigureAsync(administrator.Source, source, scenario.SourceQueue.Queue, token);
        await ConfigureAsync(administrator.Source, target, scenario.DestinationQueue.Queue, token);
        await ConfigureAsync(administrator.Target, target, scenario.DestinationQueue.Queue, token);
        var technical = await RemoteTransferDistinctIdentity.CreateAsync(administrator.Target,
            new(RemoteTransferDistinctProtocol.TechnicalSubject, target.TenantId,
                [new(target.DatabaseId, scenario.DestinationQueue.Queue,
                    RemoteTransferColdProtocol.Target)], [])
            { ClusterAdministrator = true }, token);
        var logical = await RemoteTransferDistinctIdentity.CreateAsync(administrator.Source,
            new(RemoteTransferDistinctProtocol.LogicalPrefix + Guid.NewGuid().ToString(RemoteTransferColdProtocol.TransferIdFormat),
                source.TenantId, [new(source.DatabaseId, scenario.SourceQueue.Queue, RemoteTransferColdProtocol.Source),
                    new(target.DatabaseId, scenario.DestinationQueue.Queue, RemoteTransferColdProtocol.Target)], []), token);
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, RemoteTransferDistinctProtocol.MessageId,
            RemoteTransferDistinctProtocol.Payload, RemoteTransferDistinctProtocol.Headers);
        var transfer = Guid.NewGuid();
        return new(new(scenario, logical, transfer, new(Guid.NewGuid(), source,
            [new CreateQueueTransfer(scenario.SourceQueue, transfer, scenario.DestinationQueue, message)]), message), technical);
    }

    private static async Task ConfigureAsync(KeyLoadClient administrator, PartitionRef partition, string queue,
        CancellationToken token)
        => _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId,
                new(queue, ResourceKind.WorkQueue, partition.TransactionDomainId) { QueuePolicy = new() }), token));
}
