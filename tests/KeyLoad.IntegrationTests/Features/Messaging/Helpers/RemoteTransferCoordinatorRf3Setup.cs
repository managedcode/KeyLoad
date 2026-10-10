using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Setup
{
    internal static async Task<RemoteTransferColdSeed> CreateAsync(ClusterFixture fixture, string subject,
        bool fullTarget, CancellationToken token)
    {
        QueuePolicy? policy = fullTarget ? new()
        { MaxStoredMessages = RemoteTransferCoordinatorRf3Protocol.SingleStoredMessage } : null;
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, policy, token);
        if (fullTarget)
        { await FillTargetAsync(fixture, scenario, token); }
        var identity = await MessagingRf3Identity.CreateSelectedAsync(fixture, subject, scenario.SourcePartition.TenantId,
            [new(scenario.SourcePartition.DatabaseId, scenario.SourceQueue.Queue, RemoteTransferColdProtocol.Source),
             new(scenario.DestinationPartition.DatabaseId, scenario.DestinationQueue.Queue, RemoteTransferColdProtocol.Target)],
            [MessagingRf3Scenario.WriteGrant, RemoteTransferColdProtocol.ReadGrant, RemoteTransferColdProtocol.UseGrant], true, token);
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, RemoteTransferColdProtocol.Original,
            MessagingRf3Scenario.ProtectedPayload, MessagingRf3Scenario.ProtectedHeaders);
        var transferId = Guid.NewGuid();
        return new(scenario, identity, transferId, new(Guid.NewGuid(), scenario.SourcePartition,
            [new CreateQueueTransfer(scenario.SourceQueue, transferId, scenario.DestinationQueue, message)]), message);
    }

    private static async Task FillTargetAsync(ClusterFixture fixture, MessagingRf3Scenario scenario, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(new(Guid.NewGuid(), scenario.DestinationPartition,
            [new EnqueueMessage(scenario.DestinationQueue.Queue, RemoteTransferCoordinatorRf3Protocol.Filler,
                RemoteTransferCoordinatorRf3Protocol.FillerPayload)]), token));
    }
}
