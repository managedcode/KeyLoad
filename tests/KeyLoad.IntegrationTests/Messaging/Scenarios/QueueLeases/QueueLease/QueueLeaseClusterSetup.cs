using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueLeaseClusterSetup
{
    internal static async Task<QueueLeaseClusterSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = QueueLeaseClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueLeaseClusterProtocol.Database, QueueLeaseClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueLeaseClusterProtocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var resource = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var futureLane = new QueueLaneRef(partition, QueueLeaseClusterProtocol.FutureQueue);
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(futureLane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var futureDue = TimeProvider.System.GetUtcNow().AddHours(QueueLeaseClusterProtocol.FutureScheduledHours);
        var futureGrant = new ScopeGrant(partition.DatabaseId, futureLane.Queue, QueueLeaseClusterProtocol.WorkerCapabilities);
        var grant = new ScopeGrant(partition.DatabaseId, lane.Queue, QueueLeaseClusterProtocol.WorkerCapabilities);
        var worker = await MessagingClusterIdentity.CreateAsync(fixture, tenant, [grant, futureGrant], [], false, token);
        var foreign = await MessagingClusterIdentity.CreateAsync(fixture, tenant, [grant, futureGrant], [], false, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(lane.Queue, QueueLeaseClusterProtocol.Message,
                QueueLeaseClusterProtocol.Payload, QueueLeaseClusterProtocol.Headers),
                new EnqueueMessage(futureLane.Queue, QueueLeaseClusterProtocol.FutureMessage,
                    QueueLeaseClusterProtocol.Payload, QueueLeaseClusterProtocol.Headers, NotBefore: futureDue)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(command, token));
        var future = await PublicReadAssertions.MessageAsync(await root.InspectAsync(
            new(futureLane, QueueLeaseClusterProtocol.FutureMessage), token))
            ?? throw new InvalidOperationException(QueueLeaseClusterProtocol.MissingSetup);
        return new(lane, futureLane, futureDue, future, resource, worker, foreign, command, receipt);
    }
}
