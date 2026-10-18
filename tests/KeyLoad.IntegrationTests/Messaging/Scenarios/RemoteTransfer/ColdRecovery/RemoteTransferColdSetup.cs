namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdSetup
{
    internal static async Task<RemoteTransferColdSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await MessagingClusterScenario.CreateAsync(fixture, token);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, scenario.SourcePartition.TenantId,
            [new(scenario.SourcePartition.DatabaseId, scenario.SourceQueue.Queue, RemoteTransferColdProtocol.Source),
             new(scenario.DestinationPartition.DatabaseId, scenario.DestinationQueue.Queue, RemoteTransferColdProtocol.Target)],
            [MessagingClusterScenario.WriteGrant, RemoteTransferColdProtocol.ReadGrant, RemoteTransferColdProtocol.UseGrant],
            true, token);
        var transferId = Guid.NewGuid();
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, RemoteTransferColdProtocol.Original,
            MessagingClusterScenario.ProtectedPayload, MessagingClusterScenario.ProtectedHeaders);
        var create = new CommandRequest(Guid.NewGuid(), scenario.SourcePartition,
            [new CreateQueueTransfer(scenario.SourceQueue, transferId, scenario.DestinationQueue, message)]);
        return new(scenario, identity, transferId, create, message);
    }
}
