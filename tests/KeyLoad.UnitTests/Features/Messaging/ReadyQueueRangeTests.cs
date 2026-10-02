using System.Globalization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class ReadyQueueRangeTests
{
    private const string QueueName = "jobs";
    private const string MessagePrefix = "message-";
    private const int ReadyMessageCount = 300;
    private const int BatchLimit = 256;
    private const int RequestedMessageCount = 1;

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

    private static ReceiveResult Receive(TestDatabase db, QueueLaneRef lane)
    {
        var requestId = Guid.NewGuid();
        return db.Submit(OperationKind.Receive,
            new ReceiveRequest(requestId, lane, MaxMessages: RequestedMessageCount), id: requestId)
            .Get<ReceiveResult>();
    }
}
