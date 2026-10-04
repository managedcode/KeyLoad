using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SagaTimeoutCrashScenario
{
    internal const string Mode = "saga-timeout-crash";
    internal const string OperationFile = "saga-timeout-operation.json";
    internal const string SeedTailFile = "saga-timeout-seed-tail.txt";
    internal const string Queue = "saga-work";
    internal const string TimeoutQueue = "saga-timeouts";
    internal const string SagaIdText = "af626b16-2af8-4f7d-bbbf-b23a89351ea7";
    internal const string StateJson = "{\"phase\":\"waiting\"}";
    internal const string TimeoutPayload = "{\"action\":\"expire\"}";
    internal const string TimeoutHeaders = "{\"source\":\"saga\"}";
    internal const string MessagePrefix = "saga-timeout-";
    internal const string OutboxSpace = "outbox";
    internal const string QueueCountersSpace = "queue-counters";
    internal const string PartitionId = "saga-crash";
    internal const long WaitingRevision = 1;
    internal const long TimedOutRevision = 2;
    internal const long ExpectedRevision = 1;
    internal static readonly PartitionRef Partition = new("tenant", "database", "messaging", PartitionId);
    internal static readonly Guid ConfigureWorkQueueId = Guid.Parse("be44ba62-d912-46f7-8e9f-53934859531b");
    internal static readonly Guid ConfigureTimeoutQueueId = Guid.Parse("f6831ec3-e664-43b1-94e4-9a93af3efbd0");
    internal static readonly Guid ConfigureSagaCommandId = Guid.Parse("f64ff08b-299d-42ae-a39e-b6536fdb7295");
    internal static readonly Guid ExpireCommandId = Guid.Parse("842223f3-9f76-4ba2-8a62-c3bb0f0fe8f2");

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        var lane = new QueueLaneRef(Partition, Queue);
        var timeoutLane = new QueueLaneRef(Partition, TimeoutQueue);
        ConfigureQueue(database, Queue, ConfigureWorkQueueId);
        ConfigureQueue(database, TimeoutQueue, ConfigureTimeoutQueueId);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(5);
        ConfigureSaga(database, lane, timeoutLane, deadline);
        await WaitUntilDueAsync(deadline);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, Partition).Head.Tail;
        var operation = CreateExpiryOperation(lane);
        await SaveOperationAsync(directory, tail, operation);
        boundary.Position = store.Position + 1;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static string TimeoutMessageId()
        => string.Concat(MessagePrefix, Guid.Parse(SagaIdText).ToString("N"), "-",
            WaitingRevision.ToString("x16", CultureInfo.InvariantCulture));

    private static void ConfigureQueue(DatabaseEngine database, string queue, Guid commandId)
    {
        var resource = new ResourceDefinition(queue, ResourceKind.WorkQueue, Partition.TransactionDomainId);
        var configure = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, configure, commandId)
            .Get<ResourceDefinition>();
    }

    private static void ConfigureSaga(DatabaseEngine database, QueueLaneRef lane, QueueLaneRef timeoutLane,
        DateTimeOffset deadline)
    {
        var timeout = new SagaTimeoutDefinition(timeoutLane, TimeoutPayload, TimeoutHeaders,
            TimeToLive: TimeSpan.FromDays(1));
        var saga = new CompareExchangeSaga(lane, Guid.Parse(SagaIdText), 0, SagaPhase.Waiting,
            StateJson, deadline, timeout);
        var command = new CommandRequest(ConfigureSagaCommandId, Partition, [saga]);
        _ = CrashDatabase.Submit(database, OperationKind.Batch, command, ConfigureSagaCommandId).Get<CommitReceipt>();
    }

    private static async Task WaitUntilDueAsync(DateTimeOffset deadline)
    {
        var remaining = deadline - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System);
        }
    }

    private static ReplicatedOperation CreateExpiryOperation(QueueLaneRef lane)
    {
        var command = new CommandRequest(ExpireCommandId, Partition,
            [new ExpireSaga(lane, Guid.Parse(SagaIdText), ExpectedRevision)]);
        return CrashDatabase.Operation(OperationKind.Batch, command, ExpireCommandId);
    }

    private static async Task SaveOperationAsync(string directory, long tail, ReplicatedOperation operation)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllTextAsync(Path.Combine(directory, SeedTailFile), tail.ToString(CultureInfo.InvariantCulture));
    }
}
