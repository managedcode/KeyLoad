using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateSnapshotTests
{
    private const string EventIdOne = "aggregate-event-1";
    private const string EventIdTwo = "aggregate-event-2";
    private const string EventIdThree = "aggregate-event-3";
    private const string BadChecksum = "bad-checksum";
    private const int UnknownSnapshotFormat = 99;

    [Test]
    public async Task AcEvent008SnapshotCasRetryAndReopenPreserveVersionAndExactState()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(EventIdOne), Event(EventIdTwo), Event(EventIdThree));
        var commandId = Guid.NewGuid();

        var first = fixture.StoreSnapshot(2, commandId: commandId);
        var retry = fixture.StoreSnapshot(2, commandId: commandId);
        var stored = fixture.Snapshot();
        fixture.Reopen();
        var replay = fixture.Read(maximumEvents: 3);

        await Assert.That(first.Mutations.Single().Revision).IsEqualTo(1);
        await Assert.That(retry.Mutations.Single().Revision).IsEqualTo(1);
        await Assert.That(stored.StateJson).IsEqualTo(AggregateReplayFixture.StateJson);
        await Assert.That(stored.Checksum).IsEqualTo(JsonData.Fingerprint(stored with { Checksum = string.Empty }));
        await Assert.That(replay.Snapshot!.StateJson).IsEqualTo(AggregateReplayFixture.StateJson);
        await Assert.That(replay.Events.Select(item => item.Revision).SequenceEqual([3L])).IsTrue();
    }

    [Test]
    public async Task AcEvent008SnapshotCasRejectsStaleVersionsBackwardsSourceAndBounds()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(EventIdOne), Event(EventIdTwo), Event(EventIdThree));
        fixture.StoreSnapshot(2);

        var staleCas = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(2, expectedVersion: 0));
        var backwards = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(1, expectedVersion: 1));
        var ahead = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(4, expectedVersion: 1));
        var wrongGeneration = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(2, expectedVersion: 1, generation: 2));
        fixture.RetainFrom(3);
        var erasedHistory = Assert.ThrowsExactly<KeyLoadException>(() => fixture.StoreSnapshot(1, expectedVersion: 1));

        await Assert.That(staleCas.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(backwards.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(ahead.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(wrongGeneration.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(erasedHistory.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(fixture.Snapshot().SnapshotVersion).IsEqualTo(1);
    }

    [Test]
    public async Task AcEvent008ExactExpectedVersionAdvancesTheSingleSnapshotSlot()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(EventIdOne), Event(EventIdTwo));
        fixture.StoreSnapshot(1);

        var receipt = fixture.StoreSnapshot(2, expectedVersion: 1);

        await Assert.That(receipt.Mutations.Single().Revision).IsEqualTo(2);
        await Assert.That(fixture.Snapshot().SnapshotVersion).IsEqualTo(2);
        await Assert.That(fixture.Read(maximumEvents: 1).Events).IsEmpty();
    }

    [Test]
    public async Task AcEvent008UnknownSnapshotFormatAndChecksumCorruptionFailClosed()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(EventIdOne));
        fixture.StoreSnapshot(1);
        var snapshot = fixture.Snapshot();

        fixture.StoreEnvelope(new(UnknownSnapshotFormat, snapshot));
        var unknown = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read());
        var beginning = fixture.Read(maximumEvents: 1, fromBeginning: true);
        fixture.StoreEnvelope(new(AggregateSnapshotPersistence.CurrentFormatVersion,
            snapshot with { Checksum = BadChecksum }));
        var checksum = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read());

        await Assert.That(unknown.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(beginning.Snapshot).IsNull();
        await Assert.That(beginning.Events).HasSingleItem();
        await Assert.That(checksum.Code).IsEqualTo(ErrorCode.Corruption);
    }

    private static EventData Event(string id) => new(id, AggregateReplayFixture.EventType, "{}");
}
