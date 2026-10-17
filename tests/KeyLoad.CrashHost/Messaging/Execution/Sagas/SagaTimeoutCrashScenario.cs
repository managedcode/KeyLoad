using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SagaTimeoutCrashScenario
{
    private const int SagaDueOffsetSeconds = 5;
    private const string CompactGuidFormat = "N";
    private const string ConfigureTimeoutQueueIdText = "f6831ec3-e664-43b1-94e4-9a93af3efbd0";
    private const string TimeoutIdentitySeparator = "-";
    private const string TimeoutRevisionHexFormat = "x16";

    private const string FixtureTenant = "tenant";
    private const string FixtureDatabase = "database";
    private const string MessagingDomain = "messaging";
    private const string ConfigureWorkQueueIdText = "be44ba62-d912-46f7-8e9f-53934859531b";
    private const string ConfigureSagaCommandIdText = "f64ff08b-299d-42ae-a39e-b6536fdb7295";
    private const string ExpireCommandIdText = "842223f3-9f76-4ba2-8a62-c3bb0f0fe8f2";

    private const int TimeoutMessageLifetimeDays = 1;
    [ImmutableTemporalData]
    private static readonly TimeSpan TimeoutMessageLifetime = TimeSpan.FromDays(TimeoutMessageLifetimeDays);
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
    internal static readonly PartitionRef Partition = new(FixtureTenant, FixtureDatabase, MessagingDomain, PartitionId);
    internal static readonly Guid ConfigureWorkQueueId = Guid.Parse(ConfigureWorkQueueIdText);
    internal static readonly Guid ConfigureTimeoutQueueId = Guid.Parse(ConfigureTimeoutQueueIdText);
    internal static readonly Guid ConfigureSagaCommandId = Guid.Parse(ConfigureSagaCommandIdText);
    internal static readonly Guid ExpireCommandId = Guid.Parse(ExpireCommandIdText);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int PositionStep = 1;

        var database = CrashDatabase.Create(store, tenantId: Partition.TenantId);
        var lane = new QueueLaneRef(Partition, Queue);
        var timeoutLane = new QueueLaneRef(Partition, TimeoutQueue);
        ConfigureQueue(database, Queue, ConfigureWorkQueueId);
        ConfigureQueue(database, TimeoutQueue, ConfigureTimeoutQueueId);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(SagaDueOffsetSeconds);
        ConfigureSaga(database, lane, timeoutLane, deadline);
        await WaitUntilDueAsync(deadline);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, Partition).Head.Tail;
        var operation = CreateExpiryOperation(lane);
        await SaveOperationAsync(directory, tail, operation);
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static string TimeoutMessageId()
        => string.Concat(MessagePrefix, Guid.Parse(SagaIdText).ToString(CompactGuidFormat), TimeoutIdentitySeparator,
            WaitingRevision.ToString(TimeoutRevisionHexFormat, CultureInfo.InvariantCulture));

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
        const int NoSagaRevision = 0;

        var timeout = new SagaTimeoutDefinition(timeoutLane, TimeoutPayload, TimeoutHeaders,
            TimeToLive: TimeoutMessageLifetime);
        var saga = new CompareExchangeSaga(lane, Guid.Parse(SagaIdText), NoSagaRevision, SagaPhase.Waiting,
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
