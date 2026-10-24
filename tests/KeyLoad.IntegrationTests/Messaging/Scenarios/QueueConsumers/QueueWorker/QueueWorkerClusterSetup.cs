using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterSetup
{
    internal static async Task<QueueWorkerClusterSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var partition = new PartitionRef(QueueWorkerClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueWorkerClusterProtocol.Database, QueueWorkerClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueWorkerClusterProtocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)), token));
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, lane.Queue, QueueWorkerClusterProtocol.Worker)], [], false, token);
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [.. QueueWorkerClusterProtocol.Messages.Select(id => new EnqueueMessage(lane.Queue, id,
                QueueWorkerClusterProtocol.Payload, QueueWorkerClusterProtocol.Headers))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(producer, token));
        var profile = fixture.Identity;
        var seed = new QueueWorkerClusterSeed(lane, identity, producer, receipt, Convert.FromBase64String(profile.SigningKey));
        await QueueWorkerClusterAssertions.ReceiptAsync(receipt, producer.CommandId, seed,
            [.. QueueWorkerClusterProtocol.Messages.Select(id => new MutationReceipt(QueueWorkerClusterProtocol.Enqueue,
                lane.Queue, id, QueueWorkerClusterProtocol.ReadyStateVersion))]);
        return seed;
    }
}
