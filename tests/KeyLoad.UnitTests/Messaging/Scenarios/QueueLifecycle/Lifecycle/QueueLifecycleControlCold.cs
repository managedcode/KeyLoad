using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleControlCold
{
    private const string Counters = "queue-counters";

    internal static QueueCounters Read(DatabaseEngine database, QueueLaneRef lane)
        => database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(Counters, lane.Partition, lane.Queue)))!;

    internal static void Write(DatabaseEngine database, QueueLaneRef lane, QueueCounters counters)
    {
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeySpace.Partition(Counters, lane.Partition, lane.Queue), counters);
            return true;
        });
    }

    internal static async Task FinalAsync(ZoneTreeStore store, string directory,
        QueueLifecycleRecorder recorder, QueueLaneRef lane, Func<DatabaseEngine, Task> terminal,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        recorder.Database = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await terminal(recorder.Database);
        await QueueLifecycleHistoryAssertions.ReplayAsync(recorder, lane, token);
        await terminal(recorder.Database);
    }
}
