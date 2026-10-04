using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RecurringSagaBudgetTests
{
    private const int OneRetainedRecord = 1;
    private const int RetainedByteLimit = 4_096;
    private const int LargeJsonContentLength = 2_000;
    private const string EmptyJson = "{}";

    [Test]
    public async Task RetainedRecordCapCountsSchedulesAndSagasAndReplacementPreservesCount()
    {
        using var fixture = new RecurringSagaDatabase(new() { MaxScanRecords = OneRetainedRecord });
        var scheduleId = Guid.NewGuid();
        var definition = Definition(fixture, scheduleId, EmptyJson);
        _ = fixture.Commit(fixture.Partition, new ConfigureRecurringSchedule(definition, 0));
        _ = fixture.Commit(fixture.Partition,
            new ConfigureRecurringSchedule(definition with { PayloadJson = "{\"v\":2}" }, 1));
        var before = Capacity(fixture);
        await Assert.That(before.Records).IsEqualTo(OneRetainedRecord);

        var sagaId = Guid.NewGuid();
        var result = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new CompareExchangeSaga(fixture.Queue, sagaId, 0, SagaPhase.Waiting, EmptyJson)]));
        await Assert.That(result.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!.Revision).IsEqualTo(2);
        await Assert.That(fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, sagaId)).IsNull();
        await Assert.That(Capacity(fixture).Records).IsEqualTo(OneRetainedRecord);
    }

    [Test]
    public async Task RetainedNativeBytesAreAdmittedAtomicallyAgainstCombinedLaneLimit()
    {
        using var fixture = new RecurringSagaDatabase(new() { MaxBatchBytes = RetainedByteLimit });
        var scheduleId = Guid.NewGuid();
        var largeJson = LargeJson();
        _ = fixture.Commit(fixture.Partition,
            new ConfigureRecurringSchedule(Definition(fixture, scheduleId, largeJson), 0));
        var retained = Capacity(fixture);
        await Assert.That(retained.Bytes).IsGreaterThan(0);
        await Assert.That(retained.Bytes).IsLessThan(RetainedByteLimit);

        var sagaId = Guid.NewGuid();
        var result = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new CompareExchangeSaga(fixture.Queue, sagaId, 0, SagaPhase.Waiting, largeJson)]));
        await Assert.That(result.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)).IsNotNull();
        await Assert.That(fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, sagaId)).IsNull();
        await Assert.That(Capacity(fixture).Bytes).IsEqualTo(retained.Bytes);
    }

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

    private static RecurringSagaCapacity Capacity(RecurringSagaDatabase fixture)
        => fixture.Store.Read(view => RecurringSagaStorage.RequireCapacity(view, fixture.Queue));

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id, string payload)
        => new(fixture.Queue, id, RecurringSagaDatabase.Epoch.AddSeconds(1), TimeSpan.FromSeconds(1), "UTC",
            RecurringMisfirePolicy.CatchUp, payload, EmptyJson);

    private static string LargeJson()
        => string.Concat("{\"data\":\"", new string('x', LargeJsonContentLength), "\"}");
}
