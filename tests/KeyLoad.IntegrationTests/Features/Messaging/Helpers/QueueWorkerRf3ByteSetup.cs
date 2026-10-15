using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ByteSetup
{
    internal static async Task<QueueWorkerRf3ByteSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var partition = new PartitionRef(QueueWorkerRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueWorkerRf3Protocol.Database, QueueWorkerRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueWorkerRf3Protocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            FieldPolicies = [new(QueueWorkerRf3ByteProtocol.PrivatePath, QueueWorkerRf3ByteProtocol.Classification,
                WriteGrant: QueueWorkerRf3ByteProtocol.WriteGrant, RequiredForProcessing: false)]
        };
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)), definition);
        var identity = await MessagingRf3Identity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, lane.Queue, QueueWorkerRf3Protocol.Worker)], [QueueWorkerRf3ByteProtocol.WriteGrant], false, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.ReadAtomicPartitionPlacementAsync(
            new(QueueWorkerRf3Protocol.PlacementSchemaVersion, partition), token));
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [.. QueueWorkerRf3Protocol.Messages.Select(id => new EnqueueMessage(lane.Queue, id,
                QueueWorkerRf3ByteProtocol.Payload, QueueWorkerRf3Protocol.Headers))]);
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root);
        await Assert.That(placement.Incarnation).IsEqualTo(profile.Incarnation);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(producer, token));
        var native = new QueueWorkerRf3Seed(lane, identity, placement, producer, receipt, Convert.FromBase64String(profile.SigningKey));
        await QueueWorkerRf3Assertions.ReceiptAsync(receipt, producer.CommandId, native,
            [.. QueueWorkerRf3Protocol.Messages.Select(id => new MutationReceipt(QueueWorkerRf3Protocol.Enqueue,
                lane.Queue, id, QueueWorkerRf3Protocol.ReadyStateVersion))]);
        var sizes = producer.Mutations.Cast<EnqueueMessage>().Select(message => NativeSerialization.Serialize(new MessageBody(
            message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message))).Length).ToArray();
        await Assert.That(sizes.Max()).IsLessThanOrEqualTo(QueueWorkerRf3ByteProtocol.BudgetBytes);
        await Assert.That(sizes.Min() * QueueWorkerRf3ByteProtocol.CompetingDeliveries).IsGreaterThan(QueueWorkerRf3ByteProtocol.BudgetBytes);
        var projected = Encoding.UTF8.GetByteCount(QueueWorkerRf3ByteProtocol.Projected) + Encoding.UTF8.GetByteCount(QueueWorkerRf3Protocol.Headers);
        await Assert.That(projected).IsGreaterThan(QueueWorkerRf3ByteProtocol.BudgetBytes);
        return new(native, QueueWorkerRf3ByteProtocol.Payload, QueueWorkerRf3ByteProtocol.Projected, projected);
    }
}
