using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed class DueAttemptSafetyTests
{
    private const string Payload = "{\"task\":\"run\"}";
    private const string Headers = "{}";

    [Test]
    public async Task NotDueOutcomeReplaysExactlyWhileFreshAttemptEmitsOneCanonicalOccurrence()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        var firstDue = RecurringSagaDatabase.Epoch.AddSeconds(10);
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(
            Definition(fixture, scheduleId, firstDue), 0));

        var notDueCommand = Guid.NewGuid();
        var notDue = Emit(fixture, scheduleId, 1, notDueCommand, RecurringSagaDatabase.Epoch);
        await Assert.That(Errors.Code(notDue.Problem)).IsNull();
        var storedNotDue = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, notDueCommand)
            ?? throw new InvalidOperationException("The original recurring outcome is absent.");
        var storedNotDueReceiptBytes = OrleansSerialization.Serialize(storedNotDue.Get<CommitReceipt>());
        fixture.Reopen();
        var scheduleBeforeDue = Schedule(fixture, scheduleId);
        await Assert.That(scheduleBeforeDue.NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var replayAtDue = Emit(fixture, scheduleId, 1, notDueCommand, firstDue);
        await Assert.That(Errors.Code(replayAtDue.Problem)).IsEqualTo(Errors.Code(notDue.Problem));
        await Assert.That(replayAtDue.Problem?.Detail).IsEqualTo(notDue.Problem?.Detail);
        await Assert.That(OrleansSerialization.Serialize(replayAtDue.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(notDue.Get<CommitReceipt>()).AsSpan())).IsTrue();
        var retainedNotDue = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition,
            RecurringSagaDatabase.RootPrincipal, notDueCommand)
            ?? throw new InvalidOperationException("The original not-due outcome is absent.");
        await Assert.That(OrleansSerialization.Serialize(retainedNotDue.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(storedNotDueReceiptBytes)).IsTrue();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var freshCommand = Guid.NewGuid();
        await Assert.That(freshCommand).IsNotEqualTo(notDueCommand);
        var emitted = Emit(fixture, scheduleId, 1, freshCommand, firstDue);
        await Assert.That(Errors.Code(emitted.Problem)).IsNull();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(1);
        await AssertOccurrence(fixture, scheduleId, 1, 0, firstDue);
        await Assert.That(Message(fixture, scheduleId, 1, 1)).IsNull();
    }

    [Test]
    public async Task ConcurrentFreshAttemptsCommitOneDueOrdinal()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(
            Definition(fixture, scheduleId, RecurringSagaDatabase.Epoch), 0));
        var firstCommand = Guid.NewGuid();
        var secondCommand = Guid.NewGuid();
        await Assert.That(firstCommand).IsNotEqualTo(secondCommand);

        var results = await Task.WhenAll(
            Task.Run(() => Emit(fixture, scheduleId, 1, firstCommand, RecurringSagaDatabase.Epoch)),
            Task.Run(() => Emit(fixture, scheduleId, 1, secondCommand, RecurringSagaDatabase.Epoch)));

        await Assert.That(results.All(result => Errors.Code(result.Problem) is null)).IsTrue();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(1);
        await AssertOccurrence(fixture, scheduleId, 1, 0, RecurringSagaDatabase.Epoch);
        await Assert.That(Message(fixture, scheduleId, 1, 1)).IsNull();
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, firstCommand)).IsNotNull();
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, secondCommand)).IsNotNull();
    }

    [Test]
    public async Task StaleGenerationOutcomeStaysInvalidWhileFreshGenerationEmitsOnce()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        var definition = Definition(fixture, scheduleId, RecurringSagaDatabase.Epoch);
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(definition, 0));
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(definition, 1));

        var staleCommand = Guid.NewGuid();
        var stale = Emit(fixture, scheduleId, 1, staleCommand, RecurringSagaDatabase.Epoch);
        await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        var originalError = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, staleCommand)
            ?? throw new InvalidOperationException("The original recurring outcome is absent.");
        var current = Schedule(fixture, scheduleId);
        await Assert.That(current.Generation).IsEqualTo(2);
        await Assert.That(current.NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var staleReplay = Emit(fixture, scheduleId, 1, staleCommand, RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(Errors.Code(staleReplay.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(staleReplay.Problem?.Detail).IsEqualTo(stale.Problem?.Detail);
        var retainedError = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, staleCommand)
            ?? throw new InvalidOperationException("The original recurring outcome is absent.");
        await Assert.That(Errors.Code(retainedError.Problem)).IsEqualTo(Errors.Code(originalError.Problem));
        await Assert.That(retainedError.Problem?.Detail).IsEqualTo(originalError.Problem?.Detail);
        await Assert.That(OrleansSerialization.Serialize(retainedError).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(originalError))).IsTrue();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(0);

        var freshCommand = Guid.NewGuid();
        await Assert.That(freshCommand).IsNotEqualTo(staleCommand);
        var fresh = Emit(fixture, scheduleId, 2, freshCommand, RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(Errors.Code(fresh.Problem)).IsNull();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(1);
        await AssertOccurrence(fixture, scheduleId, 2, 0, RecurringSagaDatabase.Epoch);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();
        await Assert.That(Message(fixture, scheduleId, 2, 1)).IsNull();
    }

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id,
        DateTimeOffset firstDue)
        => new(fixture.Queue, id, firstDue, TimeSpan.FromDays(1), "UTC", RecurringMisfirePolicy.CatchUp,
            Payload, Headers);

    private static Result<object> Emit(RecurringSagaDatabase fixture, Guid scheduleId, long generation,
        Guid commandId, DateTimeOffset evaluatedAt)
    {
        var request = new CommandRequest(commandId, fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, scheduleId, generation, 1)]);
        return fixture.Apply(OperationKind.Batch, request, id: commandId, time: evaluatedAt);
    }

    private static RecurringScheduleInspection Schedule(RecurringSagaDatabase fixture, Guid scheduleId)
        => fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal, fixture.Queue, scheduleId)!;

    private static MessageInspection? Message(RecurringSagaDatabase fixture, Guid scheduleId,
        long generation, long ordinal)
        => fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal, fixture.Queue,
            OccurrenceId(scheduleId, generation, ordinal));

    private static async Task AssertOccurrence(RecurringSagaDatabase fixture, Guid scheduleId,
        long generation, long ordinal, DateTimeOffset dueAt)
    {
        var message = Message(fixture, scheduleId, generation, ordinal)!;
        await Assert.That(message.Metadata.Id).IsEqualTo(OccurrenceId(scheduleId, generation, ordinal));
        await Assert.That(message.Metadata.NotBefore).IsEqualTo(dueAt);
        await Assert.That(message.PayloadJson).IsEqualTo(Payload);
        await Assert.That(message.HeadersJson).IsEqualTo(Headers);
    }

    private static string OccurrenceId(Guid scheduleId, long generation, long ordinal)
        => string.Concat("recurring-", scheduleId.ToString("N"), "-",
            generation.ToString("x16", System.Globalization.CultureInfo.InvariantCulture), "-",
            ordinal.ToString("x16", System.Globalization.CultureInfo.InvariantCulture));
}
