namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueAttemptSafetyTests
{
    private const int MaximumOccurrences = 1;
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
        await Assert.That(notDue.Error).IsNull();
        var storedNotDue = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, notDueCommand)!;
        var storedNotDueReceiptBytes = NativeSerialization.Serialize(storedNotDue.Get<CommitReceipt>());
        fixture.Reopen();
        var scheduleBeforeDue = Schedule(fixture, scheduleId);
        await Assert.That(scheduleBeforeDue.NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var replayAtDue = Emit(fixture, scheduleId, 1, notDueCommand, firstDue);
        await Assert.That(replayAtDue.Error).IsEqualTo(notDue.Error);
        await Assert.That(replayAtDue.SafeDetail).IsEqualTo(notDue.SafeDetail);
        await Assert.That(NativeSerialization.Serialize(replayAtDue.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(notDue.Get<CommitReceipt>()).AsSpan())).IsTrue();
        await Assert.That(NativeSerialization.Serialize(OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, notDueCommand)!.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(storedNotDueReceiptBytes)).IsTrue();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var freshCommand = Guid.NewGuid();
        await Assert.That(freshCommand).IsNotEqualTo(notDueCommand);
        var emitted = Emit(fixture, scheduleId, 1, freshCommand, firstDue);
        await Assert.That(emitted.Error).IsNull();
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

        await Assert.That(results.All(result => result.Error is null)).IsTrue();
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
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var originalError = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, staleCommand)!;
        var current = Schedule(fixture, scheduleId);
        await Assert.That(current.Generation).IsEqualTo(2);
        await Assert.That(current.NextOrdinal).IsEqualTo(0);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();

        var staleReplay = Emit(fixture, scheduleId, 1, staleCommand, RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(staleReplay.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(staleReplay.SafeDetail).IsEqualTo(stale.SafeDetail);
        var retainedError = OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, RecurringSagaDatabase.RootPrincipal, staleCommand)!;
        await Assert.That(retainedError.Error).IsEqualTo(originalError.Error);
        await Assert.That(retainedError.SafeDetail).IsEqualTo(originalError.SafeDetail);
        await Assert.That(retainedError.Json).IsEqualTo(originalError.Json);
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(0);

        var freshCommand = Guid.NewGuid();
        await Assert.That(freshCommand).IsNotEqualTo(staleCommand);
        var fresh = Emit(fixture, scheduleId, 2, freshCommand, RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(fresh.Error).IsNull();
        await Assert.That(Schedule(fixture, scheduleId).NextOrdinal).IsEqualTo(1);
        await AssertOccurrence(fixture, scheduleId, 2, 0, RecurringSagaDatabase.Epoch);
        await Assert.That(Message(fixture, scheduleId, 1, 0)).IsNull();
        await Assert.That(Message(fixture, scheduleId, 2, 1)).IsNull();
    }

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id,
        DateTimeOffset firstDue)
        => new(fixture.Queue, id, firstDue, TimeSpan.FromDays(1), "UTC", RecurringMisfirePolicy.CatchUp,
            Payload, Headers);

    private static OperationResult Emit(RecurringSagaDatabase fixture, Guid scheduleId, long generation,
        Guid commandId, DateTimeOffset evaluatedAt)
    {
        var request = new CommandRequest(commandId, fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, scheduleId, generation, MaximumOccurrences)]);
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
