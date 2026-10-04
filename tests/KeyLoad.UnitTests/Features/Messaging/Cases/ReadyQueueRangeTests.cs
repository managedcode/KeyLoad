using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class ReadyQueueRangeTests
{
    private const string QueueName = "jobs";
    private const string MessagePrefix = "message-";
    private const int ReadyMessageCount = 300;
    private const int BatchLimit = 256;
    private const int RequestedMessageCount = 1;
    private const int ReadyExaminationLimit = 256;
    private const int ExpiredReadyMessageCount = ReadyExaminationLimit + 1;
    private const int FirstMessageIndex = 0;
    private const int LastIndexInFirstPage = ReadyExaminationLimit - 1;
    private const int EnqueueClockOffsetSeconds = 1;
    private const int ExpiryDelaySeconds = 1;
    private const int FirstReceiveDelaySeconds = 2;
    private const int FollowupReceiveDelaySeconds = 3;
    private const int ExpiredAndLiveRecordCount = 2;
    private const long StoredAfterFirstExpiryPage = 2;
    private const long StoredAfterLiveDelivery = 1;
    private const long InFlightAfterLiveDelivery = 1;
    private const long OneStoredMessage = 1;
    private const long OneInFlightMessage = 1;
    private const long NoRangeLookaheads = 0;
    private const string ExpiredPrefix = "expired-";
    private const string LiveMessageId = "live";

    [Test]
    public async Task AcMp006SingleMessageReceiveStopsAtFirstReadyRecordAndKeepsFifo()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        for (var start = 0; start < ReadyMessageCount; start += BatchLimit)
        {
            var count = Math.Min(BatchLimit, ReadyMessageCount - start);
            var messages = Enumerable.Range(start, count)
                .Select(index => (Mutation)new EnqueueMessage(QueueName,
                    MessagePrefix + index.ToString("D3", CultureInfo.InvariantCulture), "{}"))
                .ToArray();
            db.Commit(messages);
        }

        var lane = new QueueLaneRef(db.Partition, QueueName);
        var before = db.Store.GetReadDiagnostics();
        var first = Receive(db, lane);
        var after = db.Store.GetReadDiagnostics();
        var second = Receive(db, lane);

        await Assert.That(first.Deliveries).HasSingleItem();
        await Assert.That(first.Deliveries[0].Id).IsEqualTo(MessagePrefix + "000");
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo(1L);
        await Assert.That(second.Deliveries[0].Id).IsEqualTo(MessagePrefix + "001");
    }

    [Test]
    public async Task AcMp006SingleReceiveSkipsExpiredReadyRecordBeforeFirstLiveDelivery()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        var enqueueTime = TimeProvider.System.GetUtcNow().AddSeconds(EnqueueClockOffsetSeconds);
        Enqueue(db, [
            new EnqueueMessage(QueueName, ExpiredMessageId(FirstMessageIndex), "{}",
                ExpiresAt: enqueueTime.AddSeconds(ExpiryDelaySeconds)),
            new EnqueueMessage(QueueName, LiveMessageId, "{}")
        ], enqueueTime);

        var lane = new QueueLaneRef(db.Partition, QueueName);
        var before = db.Store.GetReadDiagnostics();
        var result = Receive(db, lane, enqueueTime.AddSeconds(FirstReceiveDelaySeconds));
        var after = db.Store.GetReadDiagnostics();

        await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(result.Deliveries[0].Id).IsEqualTo(LiveMessageId);
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries)
            .IsEqualTo((long)ExpiredAndLiveRecordCount);
        await Assert.That(Metadata(db, ExpiredMessageId(FirstMessageIndex)).State).IsEqualTo(MessageState.Expired);
        await Assert.That(Counters(db).StoredMessages).IsEqualTo(OneStoredMessage);
        await Assert.That(Counters(db).InFlightMessages).IsEqualTo(OneInFlightMessage);
    }

    [Test]
    public async Task AcMp006ExpiredReadyScanHonorsTheFixed256ExaminedEntryCeiling()
    {
        using var db = new TestDatabase();
        db.Configure(QueueName, ResourceKind.WorkQueue);
        var enqueueTime = TimeProvider.System.GetUtcNow().AddSeconds(EnqueueClockOffsetSeconds);
        var expiresAt = enqueueTime.AddSeconds(ExpiryDelaySeconds);
        Enqueue(db, ExpiredThenLive(expiresAt), enqueueTime);

        var lane = new QueueLaneRef(db.Partition, QueueName);
        var before = db.Store.GetReadDiagnostics();
        var first = Receive(db, lane, enqueueTime.AddSeconds(FirstReceiveDelaySeconds));
        var after = db.Store.GetReadDiagnostics();

        await Assert.That(first.Deliveries).IsEmpty();
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries)
            .IsEqualTo((long)ReadyExaminationLimit);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(NoRangeLookaheads);
        await Assert.That(Metadata(db, ExpiredMessageId(LastIndexInFirstPage)).State)
            .IsEqualTo(MessageState.Expired);
        await Assert.That(Metadata(db, ExpiredMessageId(ReadyExaminationLimit)).State)
            .IsEqualTo(MessageState.Ready);
        await Assert.That(Counters(db).StoredMessages).IsEqualTo(StoredAfterFirstExpiryPage);

        var beforeNext = db.Store.GetReadDiagnostics();
        var second = Receive(db, lane, enqueueTime.AddSeconds(FollowupReceiveDelaySeconds));
        var afterNext = db.Store.GetReadDiagnostics();
        await Assert.That(second.Deliveries).HasSingleItem();
        await Assert.That(second.Deliveries[0].Id).IsEqualTo(LiveMessageId);
        await Assert.That(afterNext.RangeBaselineEntries - beforeNext.RangeBaselineEntries)
            .IsEqualTo((long)ExpiredAndLiveRecordCount);
        await Assert.That(Metadata(db, ExpiredMessageId(ReadyExaminationLimit)).State)
            .IsEqualTo(MessageState.Expired);
        await Assert.That(Counters(db).StoredMessages).IsEqualTo(StoredAfterLiveDelivery);
        await Assert.That(Counters(db).InFlightMessages).IsEqualTo(InFlightAfterLiveDelivery);
    }

    private static Mutation[] ExpiredThenLive(DateTimeOffset expiresAt)
        => [.. Enumerable.Range(0, ExpiredReadyMessageCount)
            .Select(index => (Mutation)new EnqueueMessage(QueueName, ExpiredMessageId(index), "{}", ExpiresAt: expiresAt)),
            new EnqueueMessage(QueueName, LiveMessageId, "{}")];

    private static string ExpiredMessageId(int index)
        => ExpiredPrefix + index.ToString("D3", CultureInfo.InvariantCulture);

    private static void Enqueue(TestDatabase db, Mutation[] messages, DateTimeOffset at)
    {
        for (var start = 0; start < messages.Length; start += BatchLimit)
        {
            var count = Math.Min(BatchLimit, messages.Length - start);
            var commandId = Guid.NewGuid();
            var batch = messages.Skip(start).Take(count).ToArray();
            db.Submit(OperationKind.Batch, new CommandRequest(commandId, db.Partition, [.. batch]),
                id: commandId, time: at).Get<CommitReceipt>();
        }
    }

    private static MessageMetadata Metadata(TestDatabase db, string id) => db.Store.Read(view =>
        view.GetRecord<MessageMetadata>(KeySpace.Partition("message-meta", db.Partition, QueueName, id))!);

    private static QueueCounters Counters(TestDatabase db) => db.Store.Read(view =>
        view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", db.Partition, QueueName))!);

    private static ReceiveResult Receive(TestDatabase db, QueueLaneRef lane, DateTimeOffset? time = null)
    {
        var requestId = Guid.NewGuid();
        return db.Submit(OperationKind.Receive,
            new ReceiveRequest(requestId, lane, MaxMessages: RequestedMessageCount), id: requestId, time: time)
            .Get<ReceiveResult>();
    }
}
