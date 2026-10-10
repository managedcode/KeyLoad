using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEnqueueColdTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueEnqueueColdProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueEnqueueColdProtocol.Queue, ResourceKind.WorkQueue,
            queuePolicy: new() { MaxStoredMessages = QueueEnqueueColdProtocol.MaximumStoredMessages });
        ConfigurePublisher(fixture.Database, fixture.Partition, Capability.QueueInspect, token);
        var scheduledAt = TimeProvider.System.GetUtcNow().AddHours(QueueEnqueueColdProtocol.FutureScheduledHours);
        var command = Original(fixture.Partition, scheduledAt);
        var original = Apply(fixture.Database, command, QueueEnqueueColdProtocol.Root, token);
        var receipt = original.Get<CommitReceipt>();
        await QueueEnqueueColdAssertions.RequireReceiptAsync(receipt, command, fixture.Store.Identity.Incarnation);
        await QueueEnqueueColdAssertions.RequireProducerAsync(fixture.Database, fixture.Partition);
        await QueueEnqueueColdAssertions.RequireInitialAsync(fixture.Database, fixture.Partition, scheduledAt);
        var image = QueueEnqueueColdAssertions.LaneBytes(fixture.Store, fixture.Partition);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Apply(database, command, QueueEnqueueColdProtocol.Root, token), original);
        await Assert.That(QueueEnqueueColdAssertions.LaneBytes(reopened, fixture.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await QueueEnqueueColdAssertions.RequireInitialAsync(database, fixture.Partition, scheduledAt);
        await RefusalsAsync(database, fixture.Partition, command, image, token);
        var lane = new QueueLaneRef(fixture.Partition, QueueEnqueueColdProtocol.Queue);
        var claimId = Guid.NewGuid();
        var delivery = database.ApplyEmbedded(new(claimId, OperationKind.Receive, QueueEnqueueColdProtocol.Root, default,
            JsonSerializer.Serialize(new ReceiveRequest(claimId, lane), JsonDefaults.Options)), token).Get<ReceiveResult>().Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(QueueEnqueueColdProtocol.Ready);
        var ackId = Guid.NewGuid();
        database.ApplyEmbedded(new(ackId, OperationKind.Delivery, QueueEnqueueColdProtocol.Root, default,
            JsonSerializer.Serialize(new DeliveryCommand(ackId, lane, delivery.Token, DeliveryAction.Ack), JsonDefaults.Options)), token).Get<CommitReceipt>();
        ConfigurePublisher(database, fixture.Partition, Capability.QueueInspect | Capability.QueuePublish, token,
            QueueEnqueueColdProtocol.RestoredEpoch);
        var healthy = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson)]);
        var healthyResult = Apply(database, healthy, QueueEnqueueColdProtocol.Publisher, token);
        healthyResult.Get<CommitReceipt>();
        await QueueEnqueueColdAssertions.RequireHealthyAsync(database, fixture.Partition, scheduledAt);
        var healthyImage = QueueEnqueueColdAssertions.LaneBytes(reopened, fixture.Partition);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Apply(restored, command, QueueEnqueueColdProtocol.Root, token), original);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Apply(restored, healthy, QueueEnqueueColdProtocol.Publisher, token), healthyResult);
        await Assert.That(QueueEnqueueColdAssertions.LaneBytes(cold, fixture.Partition)).IsEquivalentTo(healthyImage, CollectionOrdering.Matching);
        await QueueEnqueueColdAssertions.RequireHealthyAsync(restored, fixture.Partition, scheduledAt);
        await QueueEnqueueColdAssertions.RequireProducerAsync(restored, fixture.Partition);
    }

    private static async Task RefusalsAsync(DatabaseEngine database, PartitionRef partition,
        CommandRequest original, string[] image, CancellationToken token)
    {
        var changed = original with
        {
            Mutations = [new EnqueueMessage(QueueEnqueueColdProtocol.Queue,
            QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.HealthyJson)]
        };
        await Assert.That(Apply(database, changed, QueueEnqueueColdProtocol.Root, token).Error).IsEqualTo(ErrorCode.Conflict);
        var duplicate = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument, QueueEnqueueColdProtocol.HealthyJson),
                new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.HealthyJson)]);
        await QueueEnqueueColdAssertions.RequireRefusalAsync(database, duplicate, QueueEnqueueColdProtocol.Root,
            ErrorCode.Conflict, image, token);
        var quota = duplicate with
        {
            CommandId = Guid.NewGuid(),
            Mutations = [new PutDocument(QueueEnqueueColdProtocol.Collection,
            QueueEnqueueColdProtocol.RefusedDocument, QueueEnqueueColdProtocol.HealthyJson),
            new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson)]
        };
        await QueueEnqueueColdAssertions.RequireRefusalAsync(database, quota, QueueEnqueueColdProtocol.Root,
            ErrorCode.ResourceExhausted, image, token);
        var denied = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson)]);
        await QueueEnqueueColdAssertions.RequireRefusalAsync(database, denied, QueueEnqueueColdProtocol.Publisher,
            ErrorCode.PermissionDenied, image, token);
    }

    internal static OperationResult Apply(DatabaseEngine database, CommandRequest command,
        string principal, CancellationToken token) => database.ApplyEmbedded(new(command.CommandId,
            OperationKind.Batch, principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options)), token);

    private static void ConfigurePublisher(DatabaseEngine database, PartitionRef partition,
        Capability capabilities, CancellationToken token, long epoch = QueueEnqueueColdProtocol.InitialPolicyEpoch)
    {
        var id = Guid.NewGuid();
        var principal = new PrincipalRecord(QueueEnqueueColdProtocol.Publisher, partition.TenantId,
            [new(partition.DatabaseId, QueueEnqueueColdProtocol.Queue, capabilities)], [])
        { PolicyEpoch = epoch };
        database.ApplyEmbedded(new(id, OperationKind.ConfigurePrincipal, QueueEnqueueColdProtocol.Root, default,
            JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal), JsonDefaults.Options)), token).Get<PrincipalRecord>();
    }

    private static CommandRequest Original(PartitionRef partition, DateTimeOffset scheduledAt)
        => new(Guid.NewGuid(), partition, [new PutDocument(QueueEnqueueColdProtocol.Collection,
            QueueEnqueueColdProtocol.Document, QueueEnqueueColdProtocol.ReadyJson),
            new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Ready,
                QueueEnqueueColdProtocol.ReadyJson, QueueEnqueueColdProtocol.Headers),
            new EnqueueMessage(QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Scheduled,
                QueueEnqueueColdProtocol.ScheduledJson, QueueEnqueueColdProtocol.Headers, NotBefore: scheduledAt)]);
}
