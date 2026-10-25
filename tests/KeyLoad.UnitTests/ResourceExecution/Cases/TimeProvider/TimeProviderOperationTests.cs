using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.UnitTests.DocumentStorage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.ResourceExecution;

/// <summary>AC-TIME-001/002: real engine commands and queries observe their borrowed provider.</summary>
internal sealed class TimeProviderOperationTests
{
    private const string Collection = "orders";
    private const string Entity = "clock-entry";
    private const string Root = "root";
    private const string Json = "{\"clock\":true}";
    private const int DeadlineSeconds = 5;
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmbeddedCommandsPersistBorrowedUtcAndReplayOriginalOutcome(bool execution)
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new TestDatabase(timeProvider: clock);
        fixture.Configure(Collection, ResourceKind.Collection);
        var coordinator = new EmbeddedCoordinator(fixture.Database);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Partition, [new PutDocument(Collection, Entity, Json)]);
        var reference = new EntityRef(fixture.Partition, Collection, Entity);
        clock.Advance(TimeSpan.FromMinutes(1));
        var committedAt = clock.GetUtcNow();
        var outcome = await SubmitAsync(coordinator, command, execution);
        var record = DocumentCrudFixture.ReadRecord(fixture, reference)!;
        await Assert.That(record.UpdatedAt).IsEqualTo(committedAt);
        await Assert.That(record.Json).IsEqualTo(Json);
        clock.Advance(TimeSpan.FromDays(1));
        var replay = await SubmitAsync(coordinator, command, execution);
        await Assert.That(replay.Get<CommitReceipt>().Token).IsEqualTo(outcome.Get<CommitReceipt>().Token);
        await Assert.That(DocumentCrudFixture.ReadRecord(fixture, reference)!.UpdatedAt).IsEqualTo(committedAt);
        await Assert.That(fixture.Database.GetDocument(Root, reference)!.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task DueDiscoveryUsesBorrowedMonotonicClockAndFailureLeavesStoreReadable()
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new TestDatabase(timeProvider: clock);
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Commit(new PutDocument(Collection, Entity, Json));
        var before = fixture.Store.Position;
        clock.TimestampStep = TimeSpan.FromSeconds(1);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            DueWorkDiscovery.ReadPage(fixture.Database, null, Epoch));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(DueWorkProtocol.DeadlineExceeded);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        clock.TimestampStep = TimeSpan.Zero;
        var page = DueWorkDiscovery.ReadPage(fixture.Database, null, Epoch);
        await Assert.That(page.Jobs).IsEmpty();
        await Assert.That(page.Rejected).IsEmpty();
        await Assert.That(fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, Entity))!.Json).IsEqualTo(Json);
    }

    [Test]
    public async Task UtcJumpDoesNotExpireMonotonicReadBudgetButElapsedAdvanceDoes()
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new TestDatabase(timeProvider: clock);
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Commit(new PutDocument(Collection, Entity, Json));
        var budget = new ReadExecutionBudget(clock);
        budget.ConstrainLifetime(clock.GetUtcNow().AddSeconds(DeadlineSeconds));
        var before = fixture.Store.Position;
        clock.MoveUtc(Epoch.AddYears(1));
        var record = fixture.Store.Read(view => budget.CreateView(view).GetRecord<DocumentRecord>(
            KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(new(fixture.Partition, Collection, Entity))));
        await Assert.That(record!.Json).IsEqualTo(Json);
        clock.Advance(TimeSpan.FromSeconds(DeadlineSeconds + 1));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => budget.CreateView(view)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(fixture.Store.Position).IsEqualTo(before);
        await Assert.That(fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, Entity))!.Json).IsEqualTo(Json);
    }

    private static Task<Result<object>> SubmitAsync(EmbeddedCoordinator coordinator, CommandRequest command, bool execution)
        => execution
            ? coordinator.RuntimeSubmitAsync(OperationKind.Batch, command.CommandId, Root, OrleansSerialization.Serialize(command))
            : coordinator.SubmitAsync(OperationKind.Batch, command.CommandId, Root, JsonSerializer.Serialize(command, JsonDefaults.Options));
}
