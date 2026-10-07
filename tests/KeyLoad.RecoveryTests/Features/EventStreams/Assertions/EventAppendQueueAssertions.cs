using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendQueueAssertions
{
    private const string CountersSpace = "queue-counters";
    private const string BodySpace = "message-body";
    private const string ReadySpace = "ready";
    private const int MaximumRecords = 4;

    internal static async Task VerifyAsync(IAtomicStore store, bool committed, bool healthy)
    {
        var count = (committed ? 1 : 0) + (healthy ? 1 : 0);
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(CountersSpace,
            EventAppendCrashContract.Partition, EventAppendCrashContract.Queue)));
        var ready = store.Read(view => view.Scan(KeySpace.Partition(ReadySpace,
            EventAppendCrashContract.Partition, EventAppendCrashContract.Queue), MaximumRecords));
        await Assert.That(ready.HasMore).IsFalse();
        await Assert.That(ready.Records.Length).IsEqualTo(count);
        if (count == 0)
        { await Assert.That(counters).IsNull(); return; }
        await Assert.That(counters!.StoredMessages).IsEqualTo((long)count);
        await Assert.That(counters.NextReadySequence).IsEqualTo((long)count);
        await Assert.That(counters.InFlightMessages).IsEqualTo(0L);
        await Assert.That(counters.InFlightBytes).IsEqualTo(0L);
        var expected = new List<string> { EventAppendCrashContract.Producer };
        if (healthy)
        { expected.Add(EventAppendCrashContract.Healthy); }
        var actual = ready.Records.Select(record => NativeSerialization.Deserialize<string>(record.Value.Span));
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        var bodyBytes = expected.Sum(id => store.Read(view => view.ReadOwnedValue(KeySpace.Partition(
            BodySpace, EventAppendCrashContract.Partition, EventAppendCrashContract.Queue, id)))!.Length);
        await Assert.That(counters.StoredBytes).IsEqualTo((long)bodyBytes);
    }
}
