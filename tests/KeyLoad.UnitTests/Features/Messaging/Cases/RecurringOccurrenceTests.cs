using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RecurringOccurrenceTests
{
    private const string Payload = "{\"task\":\"run\"}";
    private const string Headers = "{\"tenant\":\"blue\"}";
    private const int CatchUpBound = 32;

    [Test]
    public async Task LoggedDueBoundaryEmitsContiguousStableOccurrencesAndReplayDoesNotAdvanceAgain()
    {
        using var fixture = new RecurringSagaDatabase();
        var id = Guid.NewGuid();
        var firstDue = RecurringSagaDatabase.Epoch.AddSeconds(1);
        var definition = Definition(fixture, id, firstDue, TimeSpan.FromMinutes(1));
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(definition, 0));
        await Assert.That(EmitInvalidMaximum(fixture, id, 0).Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(EmitInvalidMaximum(fixture, id, CatchUpBound + 1).Error).IsEqualTo(ErrorCode.Validation);

        var early = Emit(fixture, id, 1, 1, RecurringSagaDatabase.Epoch);
        await Assert.That(early.Mutations[0].Revision).IsEqualTo(1);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, id)!.NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, id, 1)).IsNull();

        var commandId = Guid.NewGuid();
        var due = Emit(fixture, id, 1, 2, RecurringSagaDatabase.Epoch.AddSeconds(2), commandId);
        await Assert.That(due.Mutations[0].Revision).IsEqualTo(1);
        await AssertMessage(fixture, id, 1, firstDue);
        await AssertMessage(fixture, id, 2, firstDue.AddSeconds(1));
        var replay = Emit(fixture, id, 1, 2, RecurringSagaDatabase.Epoch.AddSeconds(2), commandId);
        await Assert.That(replay.Mutations[0]).IsEqualTo(due.Mutations[0]);
        await Assert.That(replay.Token).IsEqualTo(due.Token);
        _ = Emit(fixture, id, 1, 2, RecurringSagaDatabase.Epoch.AddSeconds(2));
        await Assert.That(Message(fixture, id, 3)).IsNull();

        var next = Emit(fixture, id, 1, 32, RecurringSagaDatabase.Epoch.AddSeconds(4));
        await Assert.That(next.Mutations[0].Revision).IsEqualTo(1);
        await AssertMessage(fixture, id, 3, firstDue.AddSeconds(2));
        await AssertMessage(fixture, id, 4, firstDue.AddSeconds(3));
        await Assert.That(Message(fixture, id, 5)).IsNull();
    }

    [Test]
    public async Task CatchUpRetainsBacklogAndWatermarkAcrossRealStoreReopen()
    {
        using var fixture = new RecurringSagaDatabase();
        var id = Guid.NewGuid();
        var firstDue = RecurringSagaDatabase.Epoch;
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(Definition(fixture, id, firstDue), 0));
        _ = Emit(fixture, id, 1, CatchUpBound, RecurringSagaDatabase.Epoch.AddSeconds(100));
        await AssertMessage(fixture, id, 1, firstDue);
        await AssertMessage(fixture, id, CatchUpBound, firstDue.AddSeconds(CatchUpBound - 1));
        await Assert.That(Message(fixture, id, CatchUpBound + 1)).IsNull();

        fixture.Reopen();
        var inspection = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, id)!;
        await Assert.That(inspection.NextOrdinal).IsEqualTo(CatchUpBound);
        _ = Emit(fixture, id, inspection.Generation, CatchUpBound, RecurringSagaDatabase.Epoch.AddSeconds(100));
        await AssertMessage(fixture, id, CatchUpBound + 1, firstDue.AddSeconds(CatchUpBound));
        await AssertMessage(fixture, id, CatchUpBound * 2, firstDue.AddSeconds(CatchUpBound * 2 - 1));
        await Assert.That(Message(fixture, id, CatchUpBound * 2 + 1)).IsNull();
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, id)!.NextOrdinal).IsEqualTo(CatchUpBound * 2);
    }

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id,
        DateTimeOffset firstDue, TimeSpan? timeToLive = null)
        => new(fixture.Queue, id, firstDue, TimeSpan.FromSeconds(1), "UTC", RecurringMisfirePolicy.CatchUp,
            Payload, Headers, MessageTimeToLive: timeToLive);

    private static CommitReceipt Emit(RecurringSagaDatabase fixture, Guid id, long generation, int maximum,
        DateTimeOffset now, Guid? commandId = null)
    {
        var idempotency = commandId ?? Guid.NewGuid();
        var mutation = new EmitRecurringOccurrences(fixture.Queue, id, generation, maximum);
        var command = new CommandRequest(idempotency, fixture.Partition, [mutation]);
        return fixture.Apply(OperationKind.Batch, command, id: idempotency, time: now).Get<CommitReceipt>();
    }

    private static OperationResult EmitInvalidMaximum(RecurringSagaDatabase fixture, Guid id, int maximum)
        => fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, id, 1, maximum)]));

    private static MessageInspection? Message(RecurringSagaDatabase fixture, Guid scheduleId, long ordinal)
        => fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, 1, ordinal));

    private static async Task AssertMessage(RecurringSagaDatabase fixture, Guid scheduleId, long ordinal,
        DateTimeOffset dueAt)
    {
        var message = Message(fixture, scheduleId, ordinal)!;
        await Assert.That(message.Metadata.Id).IsEqualTo(OccurrenceId(scheduleId, 1, ordinal));
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.NotBefore).IsEqualTo(dueAt);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(dueAt.AddMinutes(1));
        await Assert.That(message.PayloadJson).IsEqualTo(Payload);
        await Assert.That(message.HeadersJson).IsEqualTo(Headers);
    }

    private static string OccurrenceId(Guid id, long generation, long ordinal)
        => string.Concat("recurring-", id.ToString("N"), "-", generation.ToString("x16"), "-", ordinal.ToString("x16"));
}
