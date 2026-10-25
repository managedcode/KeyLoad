
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class RecurringSagaBudgetTests
{
    private const string EmptyJson = "{}";

    [Test]
    public async Task CancelledInspectionHonorsCallerTokenAndHealthyInspectionRemainsAvailable()
    {
        using var fixture = new RecurringSagaDatabase();
        var scheduleId = Guid.NewGuid();
        _ = fixture.Commit(fixture.Partition,
            new ConfigureRecurringSchedule(Definition(fixture, scheduleId, EmptyJson), 0));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Database.InspectRecurringSchedule(
            RecurringSagaDatabase.RootPrincipal, fixture.Queue, scheduleId, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Database.InspectSaga(
            RecurringSagaDatabase.RootPrincipal, fixture.Queue, Guid.NewGuid(), cancellation.Token));
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!.Definition.ScheduleId).IsEqualTo(scheduleId);
    }

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id, string payload)
        => new(fixture.Queue, id, RecurringSagaDatabase.Epoch.AddSeconds(1), TimeSpan.FromSeconds(1), "UTC",
            RecurringMisfirePolicy.CatchUp, payload, EmptyJson);

}
