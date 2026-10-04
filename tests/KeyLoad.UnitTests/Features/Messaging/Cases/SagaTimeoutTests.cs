using System.Globalization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SagaTimeoutTests
{
    private const string State = "{\"phase\":\"waiting-payment\"}";
    private const string TimeoutPayload = "{\"action\":\"release-reservation\"}";
    private const string TimeoutHeaders = "{\"reason\":\"deadline\"}";

    [Test]
    public async Task LoggedDeadlineAtomicallyTimesOutSagaAndEnqueuesOneStableMessage()
    {
        using var fixture = new RecurringSagaDatabase();
        var sagaId = Guid.NewGuid();
        var deadline = RecurringSagaDatabase.Epoch.AddMinutes(2);
        var timeout = Timeout(fixture, TimeSpan.FromHours(1));
        _ = fixture.Commit(fixture.Partition,
            new CompareExchangeSaga(fixture.Queue, sagaId, 0, SagaPhase.Waiting, State, deadline, timeout));

        var early = Expire(fixture, sagaId, 1, deadline.AddTicks(-1));
        await Assert.That(early.Error).IsEqualTo(ErrorCode.Validation);
        var stillWaiting = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, sagaId)!;
        await Assert.That(stillWaiting.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(stillWaiting.Revision).IsEqualTo(1);
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal,
            fixture.TimeoutQueue, TimeoutId(sagaId, 1))).IsNull();

        var expired = Expire(fixture, sagaId, 1, deadline);
        await Assert.That(expired.Get<CommitReceipt>().Mutations[0].Revision).IsEqualTo(2);
        var after = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, sagaId)!;
        await Assert.That(after.Phase).IsEqualTo(SagaPhase.TimedOut);
        await Assert.That(after.Revision).IsEqualTo(2);
        await Assert.That(after.StateJson).IsEqualTo(State);
        await Assert.That(after.Deadline).IsEqualTo(deadline);
        var message = fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal,
            fixture.TimeoutQueue, TimeoutId(sagaId, 1))!;
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(deadline.AddHours(1));
        await Assert.That(message.PayloadJson).IsEqualTo(TimeoutPayload);
        await Assert.That(message.HeadersJson).IsEqualTo(TimeoutHeaders);
        await Assert.That(Expire(fixture, sagaId, 1, deadline).Error).IsEqualTo(ErrorCode.RevisionConflict);
    }

    [Test]
    public async Task CompletionWinsSerializedTimeoutRaceAndQueueCapacityFailureRollsBackTimeout()
    {
        using var fixture = new RecurringSagaDatabase(timeoutQueuePolicy: new() { MaxStoredMessages = 1 });
        var completedId = Guid.NewGuid();
        var deadline = RecurringSagaDatabase.Epoch.AddMinutes(1);
        _ = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, completedId, 0,
            SagaPhase.Waiting, State, deadline, Timeout(fixture, TimeSpan.FromMinutes(5))));
        _ = fixture.Commit(fixture.Partition, new CompareExchangeSaga(fixture.Queue, completedId, 1,
            SagaPhase.Completed, "{\"phase\":\"done\"}"));
        await Assert.That(Expire(fixture, completedId, 1, deadline).Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, completedId)!.Phase).IsEqualTo(SagaPhase.Completed);
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal,
            fixture.TimeoutQueue, TimeoutId(completedId, 1))).IsNull();

        var occupiedId = Guid.NewGuid();
        _ = fixture.Apply(OperationKind.Batch,
            new CommandRequest(occupiedId, fixture.Partition,
                [new EnqueueMessage(fixture.TimeoutQueue.Queue, "occupied", "{}")]), id: occupiedId, time: deadline)
            .Get<CommitReceipt>();
        var blockedId = Guid.NewGuid();
        var blockedDeadline = deadline.AddMinutes(1);
        _ = fixture.CommitAs(RecurringSagaDatabase.RootPrincipal, fixture.Partition, deadline,
            new CompareExchangeSaga(fixture.Queue, blockedId, 0,
                SagaPhase.Waiting, State, blockedDeadline, Timeout(fixture, TimeSpan.FromMinutes(5))));
        var rejected = Expire(fixture, blockedId, 1, blockedDeadline);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var unchanged = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, blockedId)!;
        await Assert.That(unchanged.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(unchanged.Revision).IsEqualTo(1);
        await Assert.That(fixture.Database.InspectMessage(RecurringSagaDatabase.RootPrincipal,
            fixture.TimeoutQueue, TimeoutId(blockedId, 1))).IsNull();
    }

    private static SagaTimeoutDefinition Timeout(RecurringSagaDatabase fixture, TimeSpan ttl)
        => new(fixture.TimeoutQueue, TimeoutPayload, TimeoutHeaders, TimeToLive: ttl);

    private static OperationResult Expire(RecurringSagaDatabase fixture, Guid sagaId, long revision,
        DateTimeOffset now)
        => fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition, [new ExpireSaga(fixture.Queue, sagaId, revision)]),
            time: now);

    private static string TimeoutId(Guid sagaId, long waitingRevision)
        => string.Concat("saga-timeout-", sagaId.ToString("N"), "-", waitingRevision.ToString("x16", CultureInfo.InvariantCulture));
}
