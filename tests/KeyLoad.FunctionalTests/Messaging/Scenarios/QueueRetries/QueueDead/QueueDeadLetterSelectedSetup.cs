using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadLetterSelectedSetup
{
    internal static async Task<QueueDeadLetterSelectedState> SeedAsync(TestDatabase fixture,
        QueueDeadLetterSelectedOperation kind, QueueDeadLetterSelectedFault fault)
    {
        fixture.Configure(QueueDeadLetterSelectedProtocol.Queue, ResourceKind.WorkQueue,
            queuePolicy: new()
            {
                MaxAttempts = QueueDeadLetterSelectedProtocol.One
            });
        fixture.Configure(QueueDeadLetterSelectedProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueDeadLetterSelectedProtocol.HistoryQueue, ResourceKind.WorkQueue);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(
            QueueDeadLetterSelectedProtocol.Administrator, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, QueueDeadLetterSelectedProtocol.HistoryQueue, Capability.All),
             new(fixture.Partition.DatabaseId, QueueDeadLetterSelectedProtocol.Queue, Capability.All),
             new(fixture.Partition.DatabaseId, QueueDeadLetterSelectedProtocol.Collection, Capability.All)], [])
        {
            ClusterAdministrator = true
        })).Get<PrincipalRecord>();
        var lane = new QueueLaneRef(fixture.Partition, QueueDeadLetterSelectedProtocol.Queue);
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var history = new List<QueueDeadLetterSelectedSaved>();
        await QueueDeadLetterSelectedRetainedInbox.SeedAsync(fixture.Database, lane, time, history);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [Message(QueueDeadLetterSelectedProtocol.Original), Message(QueueDeadLetterSelectedProtocol.Foreign)]);
        Execute(fixture.Database, history, Create(fixture.Database, OperationKind.Batch, producer, producer.CommandId, time)).Get<CommitReceipt>();
        foreach (var id in new[] { QueueDeadLetterSelectedProtocol.Original, QueueDeadLetterSelectedProtocol.Foreign })
        {
            var request = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueDeadLetterSelectedProtocol.LeaseSeconds);
            var result = Execute(fixture.Database, history, Create(fixture.Database, OperationKind.Receive, request, request.RequestId, time));
            var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
            await Assert.That(delivery).IsEqualTo(new Delivery(id, QueueDeadLetterSelectedProtocol.Payload,
                QueueDeadLetterSelectedProtocol.Headers, delivery.Token, QueueDeadLetterSelectedProtocol.One,
                time.AddSeconds(QueueDeadLetterSelectedProtocol.LeaseSeconds), QueueDeadLetterSelectedProtocol.One,
                QueueDeadLetterSelectedProtocol.One));
            var nack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Nack);
            Execute(fixture.Database, history, Create(fixture.Database, OperationKind.Delivery, nack, nack.CommandId, time)).Get<CommitReceipt>();
        }
        var target = fault is QueueDeadLetterSelectedFault.IndexAlias or QueueDeadLetterSelectedFault.WitnessMetadataAlias
            ? QueueDeadLetterSelectedProtocol.Foreign : QueueDeadLetterSelectedProtocol.Original;
        var original = Lifecycle(fixture.Database, lane, target, kind, time);
        var keys = fault == QueueDeadLetterSelectedFault.IndexAlias
            ? new[] { Key(lane, QueueDeadLetterSelectedProtocol.DeadLetter, QueueDeadLetterSelectedProtocol.Original) }
            : fault is QueueDeadLetterSelectedFault.MetadataAlias or QueueDeadLetterSelectedFault.WitnessMetadataAlias
                ? new[] { Key(lane, QueueDeadLetterSelectedProtocol.Metadata, QueueDeadLetterSelectedProtocol.Original),
                    Key(lane, QueueDeadLetterSelectedProtocol.Body, QueueDeadLetterSelectedProtocol.Original) }
                : new[] { Key(lane, QueueDeadLetterSelectedProtocol.Body, target) };
        var repairs = keys.Select(key => new QueueDeadLetterSelectedRepair(key,
            fixture.Store.Read(view => view.ReadOwnedValue(key))!)).ToArray();
        var state = new QueueDeadLetterSelectedState(lane, time, kind, fault, target, original, history.ToArray(),
            repairs, Body(QueueDeadLetterSelectedProtocol.Original), Body(QueueDeadLetterSelectedProtocol.Foreign),
            fixture.ExpectedReference(new(lane.Partition, QueueDeadLetterSelectedProtocol.Collection, target)).Lifetime!);
        await QueueDeadLetterSelectedImage.RequireAsync(fixture.Database, fixture.Store, state);
        await QueueDeadLetterSelectedRetainedInbox.RequireAsync(fixture.Database, fixture.Store, state);
        return state;
    }

    internal static void Damage(ZoneTreeStore store, QueueDeadLetterSelectedState state)
        => store.Commit((tx, _) =>
        {
            if (state.Fault == QueueDeadLetterSelectedFault.IndexAlias)
            {
                tx.PutRecord(state.Repairs[QueueDeadLetterSelectedProtocol.Zero].Key, QueueDeadLetterSelectedProtocol.Foreign);
            }
            else if (state.Fault is QueueDeadLetterSelectedFault.MetadataAlias or QueueDeadLetterSelectedFault.WitnessMetadataAlias)
            {
                tx.Put(state.Repairs[QueueDeadLetterSelectedProtocol.Zero].Key,
                    tx.ReadOwnedValue(Key(state.Lane, QueueDeadLetterSelectedProtocol.Metadata, QueueDeadLetterSelectedProtocol.Foreign))!);
                tx.Put(state.Repairs[QueueDeadLetterSelectedProtocol.One].Key,
                    tx.ReadOwnedValue(Key(state.Lane, QueueDeadLetterSelectedProtocol.Body, QueueDeadLetterSelectedProtocol.Foreign))!);
            }
            else if (state.Fault == QueueDeadLetterSelectedFault.MissingBody)
            {
                tx.Delete(state.Repairs[QueueDeadLetterSelectedProtocol.Zero].Key);
            }
            else
            {
                tx.PutRecord(state.Repairs[QueueDeadLetterSelectedProtocol.Zero].Key, state.SecondBody);
            }
            return true;
        });

    internal static Command<DatabaseCommand> Lifecycle(DatabaseEngine database, QueueLaneRef lane, string target, QueueDeadLetterSelectedOperation kind, DateTimeOffset time)
    {
        var id = Guid.NewGuid();
        Mutation mutation = kind == QueueDeadLetterSelectedOperation.Redrive
            ? new RedriveQueueMessage(lane.Queue, target, QueueDeadLetterSelectedProtocol.Three, QueueDeadLetterSelectedProtocol.One)
            : new CancelQueueMessage(lane.Queue, target, QueueDeadLetterSelectedProtocol.Three, QueueDeadLetterSelectedProtocol.One);
        return Create(database, OperationKind.Batch, new CommandRequest(id, lane.Partition, [mutation]), id, time);
    }

    internal static Result<object> Execute(DatabaseEngine database, List<QueueDeadLetterSelectedSaved> history, Command<DatabaseCommand> operation)
    {
        var result = database.Apply(operation);
        history.Add(new(operation, result));
        return result;
    }

    internal static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, T value, Guid id, DateTimeOffset time)
        => database.CreateCommand(kind, id, QueueDeadLetterSelectedProtocol.Administrator, time, JsonSerializer.Serialize(value, JsonDefaults.Options));
    internal static byte[] Key(QueueLaneRef lane, string space, params object?[] suffix)
        => KeySpace.Partition(space, lane.Partition, [lane.Queue, .. suffix]);
    internal static EnqueueMessage Message(string id) => new(QueueDeadLetterSelectedProtocol.Queue, id,
        QueueDeadLetterSelectedProtocol.Payload, QueueDeadLetterSelectedProtocol.Headers);
    internal static MessageBody Body(string id) => new(id, QueueDeadLetterSelectedProtocol.Payload,
        QueueDeadLetterSelectedProtocol.Headers, null, JsonData.Fingerprint(Message(id)));
    internal static MessageMetadata Parked(string id, long sequence) => new(id, MessageState.DeadLettered,
        QueueDeadLetterSelectedProtocol.One, QueueDeadLetterSelectedProtocol.Three, sequence, null, null,
        LeaseVersion: QueueDeadLetterSelectedProtocol.One, SafeFailureCode: QueueDeadLetterSelectedProtocol.Exhausted,
        ParkedSequence: sequence);
}
