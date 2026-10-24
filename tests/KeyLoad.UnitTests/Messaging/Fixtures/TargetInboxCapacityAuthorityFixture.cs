using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class TargetInboxCapacityAuthorityFixture
{
    internal const long Capacity = 3L;
    internal const long SecondGeneration = 2L;
    internal const string Healthy = "healthy-capacity";
    internal const string Refused = "refused-capacity";

    internal static TargetInboxCapacityAuthorityState Seed(TestDatabase source, CancellationToken token)
    {
        var seed = TargetInboxSetup.Create(source, Capacity);
        var original = TargetInboxSetup.Apply(source.Database, seed.Request, token);
        original.Get<CommitInboxResult>();
        var second = seed.Request with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = SecondGeneration,
            Effects = []
        };
        var secondResult = TargetInboxSetup.Apply(source.Database, second, token);
        secondResult.Get<CommitInboxResult>();
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Source, seed.Delivery.Token, DeliveryAction.Ack);
        var ackResult = source.Submit(OperationKind.Delivery, ack, id: ack.CommandId);
        ackResult.Get<CommitReceipt>();
        var healthy = seed.Request with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = Capacity,
            Effects = [new PutDocument(TargetInboxUnitProtocol.Collection, Healthy, TargetInboxUnitProtocol.Payload, 0L),
                new AppendEvents(TargetInboxUnitProtocol.Events, Healthy,
                    [new(Healthy, TargetInboxUnitProtocol.EventType, TargetInboxUnitProtocol.Payload)], ExpectedStreamRevision.NoStream),
                new EnqueueMessage(TargetInboxUnitProtocol.Output, Healthy, TargetInboxUnitProtocol.Payload)]
        };
        return new(seed.Request, second, healthy, Read(source.Store, TargetInboxStorage.CapacityKey(seed.Target)),
            Read(source.Store, TargetInboxStorage.Key(seed.Request)), Read(source.Store, TargetInboxStorage.Key(second)),
            original, secondResult, ack, ackResult, source.ExpectedReference(new(healthy.Target.Partition, TargetInboxUnitProtocol.Collection, Healthy)));
    }

    internal static void Change(ZoneTreeStore store, TargetInboxCapacityAuthorityState state, int fault, bool repair)
        => store.Commit((transaction, _) =>
        {
            var key = TargetInboxStorage.CapacityKey(state.Original.Target);
            if (repair)
            {
                transaction.Put(key, state.Capacity);
                transaction.Put(TargetInboxStorage.Key(state.Original), state.OriginalRecord);
                transaction.Put(TargetInboxStorage.Key(state.Second), state.SecondRecord);
            }
            else if (fault == 0)
            {
                transaction.Delete(key);
            }
            else if (fault == 4)
            {
                transaction.Delete(TargetInboxStorage.Key(state.Original));
                transaction.Delete(TargetInboxStorage.Key(state.Second));
            }
            else
            {
                transaction.PutRecord(key, DamagedCapacity(state.Capacity, fault));
            }
            return true;
        });

    private static TargetInboxCapacity DamagedCapacity(byte[] retained, int fault)
    {
        var original = OrleansSerialization.Deserialize<TargetInboxCapacity>(retained);
        return fault switch
        {
            1 => new TargetInboxCapacity(0L, 0L),
            2 => original with { Bytes = 1L },
            _ => original with { Count = 1L }
        };
    }

    private static byte[] Read(ZoneTreeStore store, byte[] key)
        => store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException("The actual target inbox seed row is absent.");
}
