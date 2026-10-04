using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SagaStateTests
{
    private const string WaitingState = "{\"step\":\"reserved\"}";
    private const string UpdatedState = "{\"step\":\"charged\"}";
    private const string CompletedState = "{\"step\":\"done\"}";

    [Test]
    public async Task SagaCasRetainsWaitingStateAcrossReopenAndTerminalStateCannotRevive()
    {
        using var fixture = new RecurringSagaDatabase();
        var sagaId = Guid.NewGuid();
        var create = fixture.Commit(fixture.Partition, Waiting(fixture, sagaId, WaitingState));
        await Assert.That(create.Mutations[0].Revision).IsEqualTo(1);
        fixture.Reopen();
        var persisted = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, sagaId)!;
        await Assert.That(persisted.Revision).IsEqualTo(1);
        await Assert.That(persisted.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(persisted.StateJson).IsEqualTo(WaitingState);
        await Assert.That(persisted.Deadline).IsNull();

        var updated = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, sagaId, 1,
            SagaPhase.Waiting, UpdatedState));
        await Assert.That(updated.Mutations[0].Revision).IsEqualTo(2);
        var completed = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, sagaId, 2,
            SagaPhase.Completed, CompletedState));
        await Assert.That(completed.Mutations[0].Revision).IsEqualTo(3);
        var after = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, sagaId)!;
        await Assert.That(after.Phase).IsEqualTo(SagaPhase.Completed);
        await Assert.That(after.StateJson).IsEqualTo(CompletedState);
        await Assert.That(after.Revision).IsEqualTo(3);

        var revived = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new CompareExchangeSaga(fixture.Queue, sagaId, 3, SagaPhase.Waiting, WaitingState)]));
        await Assert.That(revived.Error).IsEqualTo(ErrorCode.RevisionConflict);
        var directTimeout = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new CompareExchangeSaga(fixture.Queue, Guid.NewGuid(), 0, SagaPhase.TimedOut, WaitingState)]));
        await Assert.That(directTimeout.Error).IsEqualTo(ErrorCode.Validation);
        var malformedState = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new CompareExchangeSaga(fixture.Queue, Guid.NewGuid(), 0, SagaPhase.Waiting, "{\"broken\":" )]));
        await Assert.That(malformedState.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, sagaId)!.Revision).IsEqualTo(3);
    }

    private static CompareExchangeSaga Waiting(RecurringSagaDatabase fixture, Guid sagaId, string state)
        => new(fixture.Queue, sagaId, 0, SagaPhase.Waiting, state);
}
