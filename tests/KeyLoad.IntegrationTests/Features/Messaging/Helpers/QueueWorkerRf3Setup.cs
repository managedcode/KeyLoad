using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Setup
{
    internal static async Task<QueueWorkerRf3Seed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var partition = new PartitionRef(QueueWorkerRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueWorkerRf3Protocol.Database, QueueWorkerRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueWorkerRf3Protocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, lane.Queue, QueueWorkerRf3Protocol.Worker)], [], false, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(QueueWorkerRf3Protocol.PlacementSchemaVersion, partition), token));
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [.. QueueWorkerRf3Protocol.Messages.Select(id => new EnqueueMessage(lane.Queue, id,
                QueueWorkerRf3Protocol.Payload, QueueWorkerRf3Protocol.Headers))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(producer, token));
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root);
        await Assert.That(placement.Incarnation).IsEqualTo(profile.Incarnation);
        var seed = new QueueWorkerRf3Seed(lane, identity, placement, producer, receipt, Convert.FromBase64String(profile.SigningKey));
        await QueueWorkerRf3Assertions.ReceiptAsync(receipt, producer.CommandId, seed,
            [.. QueueWorkerRf3Protocol.Messages.Select(id => new MutationReceipt(QueueWorkerRf3Protocol.Enqueue,
                lane.Queue, id, QueueWorkerRf3Protocol.ReadyStateVersion))]);
        return seed;
    }
}
