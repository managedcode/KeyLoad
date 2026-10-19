using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadStagedIndexes
{
    private const string FirstSql = "SELECT * FROM staged_documents WHERE number = 1";
    private const string SecondSql = "SELECT * FROM staged_documents WHERE number = 2";
    private const string CountersFamily = "queue-counters";
    private const string BodyFamily = "message-body";
    private const string ReadyFamily = "ready";
    private const string UniqueFamily = "unique";
    private const string IndexFamily = "index";
    private const int FirstQueueOrdinal = 2;
    private const int SecondQueueOrdinal = 5;
    private const int InventoryBound = 16;

    internal static async Task CompleteAsync(AtomicTriadStagedFixture state)
    {
        var query = new QueryEngine(state.Owner, UnitExecutionOptions.QueryExecution());
        foreach (var sql in new[] { FirstSql, SecondSql })
        {
            var page = query.Execute(AtomicTriadStagedProtocol.Root, new(state.Partition, sql));
            QueryRow[] expected = sql == SecondSql
                ? [new(AtomicTriadStagedProtocol.Entity, 2L, AtomicTriadStagedProtocol.SecondJson, false, [])] : [];
            await AtomicTriadStagedCurrent.EqualAsync(page.Rows.ToArray(), expected);
            await Assert.That(page.AccessPath).IsEqualTo("index:number");
            await Assert.That(page.Cursor).IsNull();
        }
        var bodyBytes = 0L;
        for (var ordinal = 0; ordinal < AtomicTriadStagedProtocol.FinalRevision; ordinal++)
        {
            var message = (EnqueueMessage)state.Healthy.Mutations[ordinal == 0 ? FirstQueueOrdinal : SecondQueueOrdinal];
            var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, null, JsonData.Fingerprint(message));
            await AtomicTriadStagedCurrent.EqualAsync(state.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
                BodyFamily, state.Partition, AtomicTriadStagedProtocol.Queue, message.MessageId))), body);
            bodyBytes += OrleansSerialization.Serialize(body).LongLength;
        }
        await AtomicTriadStagedCurrent.EqualAsync(state.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            CountersFamily, state.Partition, AtomicTriadStagedProtocol.Queue))), new QueueCounters(2L, bodyBytes, 0L, 0L, 2L));
        var ready = state.Store.Read(view => view.Scan(KeySpace.Partition(ReadyFamily, state.Partition, AtomicTriadStagedProtocol.Queue), InventoryBound));
        await Assert.That(ready.HasMore).IsFalse();
        await AtomicTriadStagedCurrent.EqualAsync(ready.Records.Select(row => Convert.ToHexString(row.Key.Span)).ToArray(), new[]
        {
            Convert.ToHexString(KeySpace.Partition(ReadyFamily, state.Partition, AtomicTriadStagedProtocol.Queue, 1L, AtomicTriadStagedProtocol.FirstMessage)),
            Convert.ToHexString(KeySpace.Partition(ReadyFamily, state.Partition, AtomicTriadStagedProtocol.Queue, 2L, AtomicTriadStagedProtocol.SecondMessage))
        });
        await AtomicTriadStagedCurrent.EqualAsync(ready.Records.Select(row => OrleansSerialization.Deserialize<string>(row.Value.Span)).ToArray(),
            new[] { AtomicTriadStagedProtocol.FirstMessage, AtomicTriadStagedProtocol.SecondMessage });
        foreach (var family in new[] { IndexFamily, UniqueFamily })
        {
            var rows = state.Store.Read(view => view.Scan(KeySpace.Partition(family, state.Partition, AtomicTriadStagedProtocol.Collection), InventoryBound));
            await Assert.That(rows.HasMore).IsFalse();
            var row = await Assert.That(rows.Records).HasSingleItem();
            var key = family == IndexFamily
                ? KeySpace.Partition(family, state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Index, 2m, AtomicTriadStagedProtocol.Entity)
                : KeySpace.Partition(family, state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Index, 2m);
            await Assert.That(row.Key.Span.SequenceEqual(key)).IsTrue();
            await Assert.That(OrleansSerialization.Deserialize<string>(row.Value.Span)).IsEqualTo(AtomicTriadStagedProtocol.Entity);
        }
    }
}
