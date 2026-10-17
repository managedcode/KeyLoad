using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class DueFreshAttemptAssertions
{
    internal const string CreatorId = "due-fresh-creator";
    internal const string Payload = "{\"secret\":\"due-payload\"}";
    internal const string Headers = "{\"secret\":\"due-header\"}";
    private const string Classification = "due-protected";
    private const string QueueCounterSpace = "queue-counters";
    private const string EmitMutationKind = "emitRecurringOccurrences";
    private const string MissingScheduleRecord = "The recurring schedule record is unavailable.";
    private const string OccurrencePrefix = "recurring-";
    private const string GuidFormat = "N";
    private const string OrdinalFormat = "x16";
    private const string OutboxSpace = "outbox";
    private const int FirstMutationOrdinal = 0;
    private const long FirstGeneration = 1;
    private const int OneOccurrence = 1;
    private const int FirstDueSeconds = 1;
    private const int FreshCommittedSeconds = 5;

    internal static PrincipalRecord Creator() => new(CreatorId, RecurringSagaDatabase.TenantId,
        [new(RecurringSagaDatabase.DatabaseId, RecurringSagaDatabase.QueueName,
            Capability.SchedulerManage | Capability.QueuePublish)],
        [RecurringSagaDatabase.FieldWriteGrant, RecurringSagaDatabase.RawUseGrant]);

    internal static SensitiveFieldPolicy ProtectedPolicy() => new(RecurringSagaDatabase.SecretPath,
        Classification, RecurringSagaDatabase.RawReadGrant, RecurringSagaDatabase.RawUseGrant,
        RecurringSagaDatabase.FieldWriteGrant);

    internal static CommandRequest Emit(RecurringSagaDatabase fixture, Guid scheduleId, Guid commandId)
        => new(commandId, fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, scheduleId, 1, 1)]);

    internal static OperationResult Apply(RecurringSagaDatabase fixture, CommandRequest command,
        Guid commandId, DateTimeOffset evaluatedAt)
        => fixture.Apply(OperationKind.Batch, command, CreatorId, commandId, evaluatedAt);

    internal static async Task AssertDeniedAsync(RecurringSagaDatabase fixture, Guid commandId,
        OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        var stored = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, CreatorId, commandId);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Error).IsEqualTo(ErrorCode.PermissionDenied);
    }

    internal static async Task AssertNoEffectsAsync(RecurringSagaDatabase fixture, Guid scheduleId,
        byte[] scheduleBytes, QueueCounters expectedCounters, OutboxHead expectedOutbox)
    {
        var actual = fixture.Store.Read(view => view.GetRecord<RecurringScheduleRecord>(
            RecurringSagaStorage.ScheduleKey(fixture.Queue, scheduleId))!);
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(scheduleBytes)).IsTrue();
        await Assert.That(Counters(fixture)).IsEqualTo(expectedCounters);
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, 0))).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, 1))).IsNull();
        await Assert.That(fixture.Database.GetOutboxStatus(RecurringSagaDatabase.RootPrincipal,
            fixture.Partition).Head).IsEqualTo(expectedOutbox);
    }

    internal static async Task<byte[]> AssertOneEmissionAsync(RecurringSagaDatabase fixture, Guid scheduleId,
        DateTimeOffset firstDue, OperationResult result, OutboxHead expectedOutbox, byte[]? storedSnapshot = null)
    {
        await Assert.That(result.Error).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(EmitMutationKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(RecurringSagaDatabase.QueueName);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(scheduleId.ToString(GuidFormat));
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);

        var schedule = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!;
        await Assert.That(schedule.Revision).IsEqualTo(1L);
        await Assert.That(schedule.Generation).IsEqualTo(1L);
        await Assert.That(schedule.NextOrdinal).IsEqualTo(1L);
        await Assert.That(schedule.Cancelled).IsFalse();
        await Assert.That(schedule.Definition.Lane).IsEqualTo(fixture.Queue);
        await Assert.That(schedule.Definition.ScheduleId).IsEqualTo(scheduleId);
        await Assert.That(schedule.Definition.FirstDueAt).IsEqualTo(firstDue);
        await Assert.That(schedule.Definition.Interval).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(schedule.Definition.PayloadJson).IsEqualTo(Payload);
        await Assert.That(schedule.Definition.HeadersJson).IsEqualTo(Headers);
        var counters = Counters(fixture);
        await Assert.That(counters.StoredMessages).IsEqualTo(1L);
        await Assert.That(counters.StoredBytes).IsGreaterThan(0L);
        await Assert.That(counters.InFlightMessages).IsEqualTo(0L);
        await Assert.That(counters.InFlightBytes).IsEqualTo(0L);
        await Assert.That(counters.NextReadySequence).IsEqualTo(1L);
        await AssertOccurrenceAsync(fixture, scheduleId, firstDue);
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, 1))).IsNull();
        return await AssertOneOutboxEntryAsync(fixture, scheduleId, firstDue, receipt, expectedOutbox,
            storedSnapshot);
    }

    private static async Task<byte[]> AssertOneOutboxEntryAsync(RecurringSagaDatabase fixture, Guid scheduleId,
        DateTimeOffset firstDue, CommitReceipt receipt, OutboxHead before, byte[]? storedSnapshot)
    {
        var sequence = checked(before.Tail + OneOccurrence);
        var entry = new OutboxEntry(sequence, FirstMutationOrdinal, receipt.Token,
            firstDue.AddSeconds(FreshCommittedSeconds - FirstDueSeconds),
            new EmitRecurringOccurrences(fixture.Queue, scheduleId, FirstGeneration, OneOccurrence),
            new MutationReceipt(EmitMutationKind, RecurringSagaDatabase.QueueName,
                scheduleId.ToString(GuidFormat), FirstGeneration));
        var actualBytes = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxSpace,
            fixture.Partition, sequence)));
        await Assert.That(actualBytes).IsNotNull();
        await AssertOutboxFieldsAsync(NativeSerialization.Deserialize<OutboxEntry>(actualBytes), entry);
        if (storedSnapshot is not null)
        {
            await Assert.That(actualBytes.AsSpan().SequenceEqual(storedSnapshot)).IsTrue();
        }
        var expectedHead = before with
        {
            Tail = sequence,
            StoredRecords = checked(before.StoredRecords + OneOccurrence),
            StoredBytes = checked(before.StoredBytes + actualBytes!.Length)
        };
        await Assert.That(fixture.Database.GetOutboxStatus(RecurringSagaDatabase.RootPrincipal,
            fixture.Partition).Head).IsEqualTo(expectedHead);
        return actualBytes!;
    }

    private static async Task AssertOutboxFieldsAsync(OutboxEntry actual, OutboxEntry expected)
    {
        await Assert.That(actual.Sequence).IsEqualTo(expected.Sequence);
        await Assert.That(actual.Ordinal).IsEqualTo(expected.Ordinal);
        await Assert.That(actual.Commit).IsEqualTo(expected.Commit);
        await Assert.That(actual.CommittedAt).IsEqualTo(expected.CommittedAt);
        await Assert.That(actual.Mutation).IsEqualTo(expected.Mutation);
        await Assert.That(actual.Receipt.Kind).IsEqualTo(expected.Receipt.Kind);
        await Assert.That(actual.Receipt.Resource).IsEqualTo(expected.Receipt.Resource);
        await Assert.That(actual.Receipt.Id).IsEqualTo(expected.Receipt.Id);
        await Assert.That(actual.Receipt.Revision).IsEqualTo(expected.Receipt.Revision);
        await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
        await Assert.That(actual.Before).IsNull();
        await Assert.That(actual.After).IsNull();
    }

    internal static async Task AssertReplayAsync(OperationResult original, OperationResult replay)
    {
        await Assert.That(replay.Error).IsNull();
        var first = original.Get<CommitReceipt>();
        var again = replay.Get<CommitReceipt>();
        await Assert.That(again.Token).IsEqualTo(first.Token);
        await Assert.That(NativeSerialization.Serialize(again).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(first))).IsTrue();
    }

    internal static byte[] ScheduleBytes(RecurringSagaDatabase fixture, Guid scheduleId)
        => fixture.Store.Read(view =>
        {
            var record = view.GetRecord<RecurringScheduleRecord>(RecurringSagaStorage.ScheduleKey(fixture.Queue, scheduleId));
            return NativeSerialization.Serialize(record
                ?? throw new InvalidOperationException(MissingScheduleRecord));
        });

    internal static QueueCounters Counters(RecurringSagaDatabase fixture)
        => fixture.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueCounterSpace,
            fixture.Partition, RecurringSagaDatabase.QueueName)) ?? new(0, 0, 0, 0, 0));

    private static async Task AssertOccurrenceAsync(RecurringSagaDatabase fixture, Guid scheduleId,
        DateTimeOffset firstDue)
    {
        var message = fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, 0));
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.Id).IsEqualTo(OccurrenceId(scheduleId, 0));
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.NotBefore).IsEqualTo(firstDue);
        await Assert.That(message.Metadata.ExpiresAt).IsNull();
        await Assert.That(message.PayloadJson).IsEqualTo(Payload);
        await Assert.That(message.HeadersJson).IsEqualTo(Headers);
    }

    private static string OccurrenceId(Guid scheduleId, long ordinal)
        => string.Concat(OccurrencePrefix, scheduleId.ToString(GuidFormat), "-",
            1L.ToString(OrdinalFormat, System.Globalization.CultureInfo.InvariantCulture), "-",
            ordinal.ToString(OrdinalFormat, System.Globalization.CultureInfo.InvariantCulture));
}
