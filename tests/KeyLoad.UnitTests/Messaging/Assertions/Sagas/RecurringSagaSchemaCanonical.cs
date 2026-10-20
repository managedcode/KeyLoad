using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.Messaging.RecurringSagaSchemaProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RecurringSagaSchemaCanonical
{
    internal static async Task CurrentAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
    {
        var lane = state.Definition.Lane;
        var schedule = new RecurringScheduleRecord(lane, ScheduleId, Scheduler, state.Definition, One, One, Zero, false);
        var saga = new SagaRecord(lane, SagaId, Scheduler, One, SagaPhase.Waiting, Body, null, null);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<RecurringScheduleRecord>(
            RecurringSagaStorage.ScheduleKey(lane, ScheduleId))), schedule);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<SagaRecord>(
            RecurringSagaStorage.SagaKey(lane, SagaId))), saga);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<RecurringSagaCapacity>(
            RecurringSagaStorage.CapacityKey(lane))), new RecurringSagaCapacity(Two,
                OrleansSerialization.Serialize(schedule).LongLength + OrleansSerialization.Serialize(saga).LongLength));
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(lane.Partition.TenantId, lane.Partition.DatabaseId, Queue))), state.Resource);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(Reader))), state.Reader);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(Denied))), state.Denied);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(Scheduler))), state.Scheduler);
        await Assert.That(state.Scheduler.ClusterAdministrator).IsFalse();
        await Assert.That(state.Scheduler.PolicyEpoch).IsEqualTo(InitialEpoch);
        await Assert.That(state.Scheduler.TenantId).IsEqualTo(lane.Partition.TenantId);
        await Assert.That(state.Reader.ClusterAdministrator).IsFalse();
        await PrivateReferencesAsync(state.Seed.Result.Get<CommitReceipt>());
        await OutboxAsync(owner, state);
        foreach (var command in state.History)
        {
            await RecurringSagaSchemaReplay.StoredAsync(owner, command);
        }
    }

    internal static async Task PrivateReferencesAsync(CommitReceipt receipt)
    {
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }

    private static async Task OutboxAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
    {
        var seed = state.Seed;
        var expected = new OutboxEntry[]
        {
            new(One, Zero, seed.ExpectedToken, new DateTimeOffset(seed.Operation.Timestamp), state.ScheduleMutation,
                new(ScheduleKind, Queue, ScheduleId.ToString(Format), One)),
            new(Two, One, seed.ExpectedToken, new DateTimeOffset(seed.Operation.Timestamp), state.SagaMutation,
                new(SagaKind, Queue, SagaId.ToString(Format), One))
        };
        var partition = owner.Source.Partition;
        var page = owner.Store.Read(view => view.Scan(KeySpace.Partition(Outbox, partition), owner.Database.Limits.MaxScanRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(Two);
        foreach (var (row, literal) in page.Records.Zip(expected))
        {
            await Assert.That(row.Key.Span.SequenceEqual(KeySpace.Partition(Outbox, partition, literal.Sequence))).IsTrue();
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(row.Value.Span);
            await SafeProjectorOperationSeed.EqualAsync(actual, literal);
            await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
        }
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<OutboxHead>(
            KeySpace.Partition(OutboxHeadSpace, partition))), new OutboxHead(Two, One, Two,
                expected.Sum(row => OrleansSerialization.Serialize(row).LongLength)));
    }
}
