using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneReceiveCancellationProvisioning
{
    internal static async Task<MultiLaneReceiveRequest> SeedAsync(KeyLoadClient administrator,
        RequestCqrsPhaseFaultIdentity identity, CancellationToken token)
    {
        var queues = new[] { "cancel-first", "cancel-second" };
        var partition = identity.Partition;
        var principal = new PrincipalRecord(identity.PrincipalId, partition.TenantId,
            [.. queues.Select(queue => new ScopeGrant(partition.DatabaseId, queue,
                Capability.QueueConsume | Capability.QueueAck))], [])
        { ClusterAdministrator = false };
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(stored.ClusterAdministrator).IsFalse();
        await Assert.That(stored.Id).IsEqualTo(identity.PrincipalId);
        foreach (var queue in queues)
        {
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId,
                    new ResourceDefinition(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token)
                .ConfigureAwait(false)).ConfigureAwait(false);
            await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), partition,
                [new EnqueueMessage(queue, MultiLaneReceiveRf3Flow.Message, MultiLaneReceiveRf3Flow.Payload,
                    MultiLaneReceiveRf3Flow.Headers)]), token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        return new(Guid.NewGuid(), [.. queues.Select(queue => new ReceiveRequest(Guid.NewGuid(), new(partition, queue)))]);
    }
}
