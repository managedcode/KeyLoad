using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Setup
{
    internal static async Task<QueueLeaseRf3Seed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = QueueLeaseRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueLeaseRf3Protocol.Database, QueueLeaseRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueLeaseRf3Protocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var resource = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var futureLane = new QueueLaneRef(partition, QueueLeaseRf3Protocol.FutureQueue);
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(futureLane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var futureDue = TimeProvider.System.GetUtcNow().AddHours(QueueLeaseRf3Protocol.FutureScheduledHours);
        var futureGrant = new ScopeGrant(partition.DatabaseId, futureLane.Queue, QueueLeaseRf3Protocol.WorkerCapabilities);
        var grant = new ScopeGrant(partition.DatabaseId, lane.Queue, QueueLeaseRf3Protocol.WorkerCapabilities);
        var worker = await MessagingRf3Identity.CreateAsync(fixture, tenant, [grant, futureGrant], [], false, token);
        var foreign = await MessagingRf3Identity.CreateAsync(fixture, tenant, [grant, futureGrant], [], false, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(lane.Queue, QueueLeaseRf3Protocol.Message,
                QueueLeaseRf3Protocol.Payload, QueueLeaseRf3Protocol.Headers),
                new EnqueueMessage(futureLane.Queue, QueueLeaseRf3Protocol.FutureMessage,
                    QueueLeaseRf3Protocol.Payload, QueueLeaseRf3Protocol.Headers, NotBefore: futureDue)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(command, token));
        var future = await McpCallerAssertions.SdkSuccessAsync(await root.InspectAsync(
            new(futureLane, QueueLeaseRf3Protocol.FutureMessage), token))
            ?? throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup);
        return new(lane, futureLane, futureDue, future, resource, worker, foreign, command, receipt);
    }
}
