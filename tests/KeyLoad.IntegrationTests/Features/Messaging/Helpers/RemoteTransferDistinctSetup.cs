using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctSetup
{
    internal static async Task<RemoteTransferDistinctSeed> CreateAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed parent, CancellationToken token)
    {
        var target = parent.Partition;
        var source = new PartitionRef(target.TenantId, target.DatabaseId, target.TransactionDomainId,
            Guid.NewGuid().ToString(RemoteTransferColdProtocol.TransferIdFormat));
        var scenario = new MessagingRf3Scenario(source, target);
        await ConfigureAsync(parent.Source, source, scenario.SourceQueue.Queue, token);
        await ConfigureAsync(parent.Source, target, scenario.DestinationQueue.Queue, token);
        await ConfigureAsync(parent.Target, target, scenario.DestinationQueue.Queue, token);
        using var targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var targetRoot = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var technical = await RemoteTransferDistinctIdentity.CreateAsync(targetRoot, new PrincipalRecord(RemoteTransferDistinctProtocol.TechnicalSubject,
                target.TenantId, [new(target.DatabaseId, scenario.DestinationQueue.Queue,
                    RemoteTransferDistinctProtocol.TargetCapabilities | Capability.QueueConsume)], [])
        { ClusterAdministrator = true }, token);
        var identity = await RemoteTransferDistinctIdentity.CreateAsync(parent.Source,
            new PrincipalRecord(RemoteTransferDistinctProtocol.LogicalPrefix
                + Guid.NewGuid().ToString(RemoteTransferColdProtocol.TransferIdFormat), source.TenantId,
                [new(source.DatabaseId, scenario.SourceQueue.Queue, RemoteTransferDistinctProtocol.SourceCapabilities),
                    new(target.DatabaseId, scenario.DestinationQueue.Queue, RemoteTransferDistinctProtocol.TargetCapabilities)], []), token);
        await Assert.That(identity.Principal.Id).IsNotEqualTo(RemoteTransferDistinctProtocol.TechnicalSubject);
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, RemoteTransferDistinctProtocol.MessageId,
            RemoteTransferDistinctProtocol.Payload, RemoteTransferDistinctProtocol.Headers);
        var transferId = Guid.NewGuid();
        var create = new CommandRequest(Guid.NewGuid(), source,
            [new CreateQueueTransfer(scenario.SourceQueue, transferId, scenario.DestinationQueue, message)]);
        return new(new(scenario, identity, transferId, create, message), technical);
    }

    private static async Task ConfigureAsync(KeyLoadClient administrator, PartitionRef partition, string queue,
        CancellationToken token)
    {
        var resource = new ResourceDefinition(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        { QueuePolicy = new() };
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
    }
}
