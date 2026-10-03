using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplaySequenceTests
{
    private const string FirstEventId = "replay-sequence-1";
    private const string SecondEventId = "replay-sequence-2";
    private const string ThirdEventId = "replay-sequence-3";

    [Test]
    public async Task AcEvent007DuplicateOrReversedEventSequencesAreCorruption()
    {
        using var duplicate = new AggregateReplayFixture();
        duplicate.Append(Event(FirstEventId), Event(SecondEventId));
        RewriteSequence(duplicate, 2, 1);
        var duplicateError = Assert.ThrowsExactly<KeyLoadException>(() => duplicate.Read(maximumEvents: 2));

        using var reversed = new AggregateReplayFixture();
        reversed.Append(Event(FirstEventId), Event(SecondEventId), Event(ThirdEventId));
        RewriteSequence(reversed, 2, 3);
        RewriteSequence(reversed, 3, 2);
        var reversedError = Assert.ThrowsExactly<KeyLoadException>(() => reversed.Read(maximumEvents: 3));

        await Assert.That(duplicateError.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(reversedError.Code).IsEqualTo(ErrorCode.Corruption);
    }

    private static EventData Event(string eventId)
        => new(eventId, AggregateReplayFixture.EventType, "{}");

    private static void RewriteSequence(AggregateReplayFixture fixture, long revision, long sequence)
        => fixture.Store.Commit((transaction, _) =>
        {
            var key = fixture.EventKey(revision);
            var record = transaction.GetRecord<EventRecord>(key) ?? throw new InvalidOperationException();
            transaction.PutRecord(key, record with { EventSequence = sequence });
            return true;
        });
}
