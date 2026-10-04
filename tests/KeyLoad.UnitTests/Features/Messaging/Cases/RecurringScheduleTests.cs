
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RecurringScheduleTests
{
    private const string Payload = "{\"job\":\"first\"}";
    private const string ReplacementPayload = "{\"job\":\"replacement\"}";
    private const string UtcZone = "UTC";

    [Test]
    public async Task ScheduleRevisionGenerationAndCancellationAreExplicitCas()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        var definition = Definition(fixture, scheduleId, Payload);
        var created = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(definition, 0));
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1);
        var first = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!;
        await Assert.That(first.Generation).IsEqualTo(1);
        await Assert.That(first.NextOrdinal).IsEqualTo(0);

        var stale = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new ConfigureRecurringSchedule(definition with { PayloadJson = ReplacementPayload }, 0)]));
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.RevisionConflict);
        var replacement = fixture.Commit(fixture.Partition,
            new ConfigureRecurringSchedule(definition with { PayloadJson = ReplacementPayload }, 1));
        await Assert.That(replacement.Mutations[0].Revision).IsEqualTo(2);
        var revised = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!;
        await Assert.That(revised.Generation).IsEqualTo(2);
        await Assert.That(revised.NextOrdinal).IsEqualTo(0);
        await Assert.That(revised.Definition.PayloadJson).IsEqualTo(ReplacementPayload);

        var cancelled = fixture.Commit(fixture.Partition, new CancelRecurringSchedule(fixture.Queue, scheduleId, 2));
        await Assert.That(cancelled.Mutations[0].Revision).IsEqualTo(3);
        var afterCancel = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!;
        await Assert.That(afterCancel.Cancelled).IsTrue();
        await Assert.That(afterCancel.NextOrdinal).IsEqualTo(0);
        var emit = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, scheduleId, 2, 1)]));
        await Assert.That(emit.Error).IsEqualTo(ErrorCode.RevisionConflict);
        var reactivated = fixture.Commit(fixture.Partition,
            new ConfigureRecurringSchedule(definition with { PayloadJson = Payload }, 3));
        await Assert.That(reactivated.Mutations[0].Revision).IsEqualTo(4);
        var active = fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!;
        await Assert.That(active.Generation).IsEqualTo(3);
        await Assert.That(active.Cancelled).IsFalse();
        await Assert.That(active.NextOrdinal).IsEqualTo(0);
        var staleGeneration = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EmitRecurringOccurrences(fixture.Queue, scheduleId, 2, 1)]));
        await Assert.That(staleGeneration.Error).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task InvalidProfilesAndOverflowingIntervalsFailWithoutRetainingState()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        var valid = Definition(fixture, scheduleId, Payload);
        var invalidDefinitions = new[]
        {
            valid with { TimeZone = "Europe/Paris" },
            valid with { Misfire = (RecurringMisfirePolicy)int.MaxValue },
            valid with { Interval = TimeSpan.FromMilliseconds(999) },
            valid with { Interval = TimeSpan.FromDays(366) },
            valid with { MessageTimeToLive = TimeSpan.FromDays(366) },
            valid with { PayloadJson = "{\"broken\":" },
            valid with { HeadersJson = "{\"broken\":" }
        };
        foreach (var invalid in invalidDefinitions)
        {
            var result = fixture.Apply(OperationKind.Batch,
                new CommandRequest(Guid.NewGuid(), fixture.Partition, [new ConfigureRecurringSchedule(invalid, 0)]));
            await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        }
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)).IsNull();
    }

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid scheduleId,
        string payload)
        => new(fixture.Queue, scheduleId, RecurringSagaDatabase.Epoch.AddSeconds(10), TimeSpan.FromSeconds(1),
            UtcZone, RecurringMisfirePolicy.CatchUp, payload, "{}", MessageTimeToLive: TimeSpan.FromMinutes(5));
}
