using System.Text;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterByteSetup
{
    internal static async Task<QueueWorkerClusterByteSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var partition = new PartitionRef(QueueWorkerClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueWorkerClusterProtocol.Database, QueueWorkerClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueWorkerClusterProtocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            FieldPolicies = [new(QueueWorkerClusterByteProtocol.PrivatePath, QueueWorkerClusterByteProtocol.Classification,
                WriteGrant: QueueWorkerClusterByteProtocol.WriteGrant, RequiredForProcessing: false)]
        };
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)), definition);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, lane.Queue, QueueWorkerClusterProtocol.Worker)], [QueueWorkerClusterByteProtocol.WriteGrant], false, token);
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [.. QueueWorkerClusterProtocol.Messages.Select(id => new EnqueueMessage(lane.Queue, id,
                QueueWorkerClusterByteProtocol.Payload, QueueWorkerClusterProtocol.Headers))]);
        var profile = fixture.Identity;
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(producer, token));
        var execution = new QueueWorkerClusterSeed(lane, identity, producer, receipt, Convert.FromBase64String(profile.SigningKey));
        await QueueWorkerClusterAssertions.ReceiptAsync(receipt, producer.CommandId, execution,
            [.. QueueWorkerClusterProtocol.Messages.Select(id => new MutationReceipt(QueueWorkerClusterProtocol.Enqueue,
                lane.Queue, id, QueueWorkerClusterProtocol.ReadyStateVersion))]);
        var sizes = producer.Mutations.Cast<EnqueueMessage>().Select(message => OrleansSerialization.Serialize(new MessageBody(
            message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message))).Length).ToArray();
        await Assert.That(sizes.Max()).IsLessThanOrEqualTo(QueueWorkerClusterByteProtocol.BudgetBytes);
        await Assert.That(sizes.Min() * QueueWorkerClusterByteProtocol.CompetingDeliveries).IsGreaterThan(QueueWorkerClusterByteProtocol.BudgetBytes);
        var projected = Encoding.UTF8.GetByteCount(QueueWorkerClusterByteProtocol.Projected) + Encoding.UTF8.GetByteCount(QueueWorkerClusterProtocol.Headers);
        await Assert.That(projected).IsGreaterThan(QueueWorkerClusterByteProtocol.BudgetBytes);
        return new(execution, QueueWorkerClusterByteProtocol.Payload, QueueWorkerClusterByteProtocol.Projected, projected);
    }
}
