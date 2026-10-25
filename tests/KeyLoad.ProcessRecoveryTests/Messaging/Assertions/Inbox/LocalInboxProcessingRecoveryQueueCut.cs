using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class LocalInboxProcessingRecoveryQueueCut
{
    private const int LaneRowBound = 1;
    private const long EmptyUsage = 0L;
    private const long StoredOutputCount = 1L;
    private const int OriginalClaimAttempt = 1;
    private const int NoOutputAttempts = 0;
    private const long ReadyStateVersion = 1L;
    private const long InputAckStateVersion = 3L;
    private const long FirstReadySequence = 1L;
    private const long OriginalLeaseVersion = 1L;
    private const long NoOutputLeaseVersion = 0L;
    private const long OriginalGeneration = 1L;
    private const string EmptyHeaders = "{}";
    private const string MetadataFamily = "message-meta";
    private const string BodyFamily = "message-body";
    private const string CountersFamily = "queue-counters";
    private const string ReadyFamily = "ready";
    private static readonly string[] InactiveFamilies =
        ["scheduled", "lease", "dead-letter", "queue-pending-dead-letter", "queue-dead-letter-order", "queue-order"];

    internal static async Task RequireAsync(IAtomicStore store)
    {
        var input = LocalInboxProcessingCrashContract.InputLane;
        var output = LocalInboxProcessingCrashContract.OutputLane;
        var id = LocalInboxProcessingCrashContract.Message;
        var body = LiteralBody();
        var inputMetadata = new MessageMetadata(id, MessageState.Acked, OriginalClaimAttempt,
            InputAckStateVersion, FirstReadySequence, null, null, LeaseVersion: OriginalLeaseVersion,
            DeliveryGeneration: OriginalGeneration);
        var outputMetadata = new MessageMetadata(id, MessageState.Ready, NoOutputAttempts,
            ReadyStateVersion, FirstReadySequence, null, null, LeaseVersion: NoOutputLeaseVersion,
            DeliveryGeneration: OriginalGeneration);
        await RowsAsync(store, input, MetadataFamily, [Row(Key(input, MetadataFamily, id), inputMetadata)]);
        await RowsAsync(store, output, MetadataFamily, [Row(Key(output, MetadataFamily, id), outputMetadata)]);
        await RowsAsync(store, input, BodyFamily, []);
        await RowsAsync(store, output, BodyFamily, [Row(Key(output, BodyFamily, id), body)]);
        await RowsAsync(store, input, CountersFamily, [Row(Key(input, CountersFamily),
            new QueueCounters(EmptyUsage, EmptyUsage, EmptyUsage, EmptyUsage, FirstReadySequence))]);
        await RowsAsync(store, output, CountersFamily, [Row(Key(output, CountersFamily),
            new QueueCounters(StoredOutputCount, OrleansSerialization.Serialize(body).LongLength,
                EmptyUsage, EmptyUsage, FirstReadySequence))]);
        await RowsAsync(store, input, ReadyFamily, []);
        await RowsAsync(store, output, ReadyFamily, [Row(Key(output, ReadyFamily, FirstReadySequence, id), id)]);
        foreach (var lane in new[] { input, output })
        {
            foreach (var family in InactiveFamilies)
            {
                await RowsAsync(store, lane, family, []);
            }
        }
    }

    private static MessageBody LiteralBody()
    {
        var enqueue = new EnqueueMessage(LocalInboxProcessingCrashContract.Output,
            LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.Payload);
        return new(enqueue.MessageId, LocalInboxProcessingCrashContract.Payload, EmptyHeaders,
            null, JsonData.Fingerprint(enqueue));
    }

    private static byte[] Key(QueueLaneRef lane, string family, params object?[] tail)
        => KeySpace.Partition(family, lane.Partition, new object?[] { lane.Queue }.Concat(tail).ToArray());

    private static string Row<T>(byte[] key, T value)
        => Convert.ToHexString(key) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(value));

    private static async Task RowsAsync(IAtomicStore store, QueueLaneRef lane, string family, string[] expected)
    {
        var page = store.Read(view => view.Scan(Key(lane, family), LaneRowBound));
        await Assert.That(page.HasMore).IsFalse();
        var rows = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":"
            + Convert.ToHexString(row.Value.Span)).ToArray();
        await Assert.That(rows).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
