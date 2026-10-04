using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class RecurringScheduleCrashScenario
{
    internal const string Mode = "recurring-schedule-crash";
    internal const string OperationFile = "recurring-schedule-operation.json";
    internal const string SeedTailFile = "recurring-schedule-seed-tail.txt";
    internal const string Queue = "scheduled-work";
    internal const string ScheduleIdText = "59b1031d-feb3-4d60-a0cf-c09351472a38";
    internal const string MessagePayload = "{\"operation\":\"scheduled\"}";
    internal const string MessageHeaders = "{\"source\":\"recurring\"}";
    internal const string MessagePrefix = "recurring-";
    internal const string OutboxSpace = "outbox";
    internal const string QueueCountersSpace = "queue-counters";
    internal const string PartitionId = "schedule-crash";
    internal const long Generation = 1;
    internal const long FirstOrdinal = 0;
    internal const int MaxOccurrences = 1;
    internal static readonly PartitionRef Partition = new("tenant", "database", "messaging", PartitionId);
    internal static readonly Guid ConfigureQueueCommandId = Guid.Parse("509b9488-45b9-457c-9c92-a7c851956c64");
    internal static readonly Guid ConfigureCommandId = Guid.Parse("a0f9582f-f9af-4efa-8334-b7e8af4cba73");
    internal static readonly Guid EmitCommandId = Guid.Parse("563f51c8-dc8c-46f0-8af4-56d2e3bf0767");

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var database = CrashDatabase.Create(store);
        var lane = new QueueLaneRef(Partition, Queue);
        ConfigureQueue(database);
        var firstDue = TimeProvider.System.GetUtcNow().AddMinutes(-1);
        ConfigureSchedule(database, lane, firstDue);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, Partition).Head.Tail;
        var operation = CreateEmitOperation(lane);
        await SaveOperationAsync(directory, tail, operation);
        ArmBoundary(store, boundary);
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static string OccurrenceId(long generation, long ordinal)
        => string.Concat(MessagePrefix, Guid.Parse(ScheduleIdText).ToString("N"), "-",
            generation.ToString("x16", CultureInfo.InvariantCulture), "-", ordinal.ToString("x16", CultureInfo.InvariantCulture));

    private static void ConfigureQueue(DatabaseEngine database)
    {
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId);
        var configure = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, configure, ConfigureQueueCommandId)
            .Get<ResourceDefinition>();
    }

    private static void ConfigureSchedule(DatabaseEngine database, QueueLaneRef lane, DateTimeOffset firstDue)
    {
        var definition = new RecurringScheduleDefinition(lane, Guid.Parse(ScheduleIdText), firstDue,
            TimeSpan.FromDays(1), "UTC", RecurringMisfirePolicy.CatchUp, MessagePayload, MessageHeaders);
        var command = new CommandRequest(ConfigureCommandId, Partition, [new ConfigureRecurringSchedule(definition, 0)]);
        _ = CrashDatabase.Submit(database, OperationKind.Batch, command, ConfigureCommandId).Get<CommitReceipt>();
    }

    private static ReplicatedOperation CreateEmitOperation(QueueLaneRef lane)
    {
        var command = new CommandRequest(EmitCommandId, Partition,
            [new EmitRecurringOccurrences(lane, Guid.Parse(ScheduleIdText), Generation, MaxOccurrences)]);
        return CrashDatabase.Operation(OperationKind.Batch, command, EmitCommandId);
    }

    private static async Task SaveOperationAsync(string directory, long tail, ReplicatedOperation operation)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllTextAsync(Path.Combine(directory, SeedTailFile), tail.ToString(CultureInfo.InvariantCulture));
    }

    private static void ArmBoundary(ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        boundary.Position = store.Position + 1;
        boundary.Armed = true;
    }
}
