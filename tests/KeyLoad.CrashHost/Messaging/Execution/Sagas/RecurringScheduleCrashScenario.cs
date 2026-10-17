using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class RecurringScheduleCrashScenario
{
    private const string FixtureTenant = "tenant";
    private const string FixtureDatabase = "database";
    private const string MessagingDomain = "messaging";
    private const string ConfigureQueueCommandIdText = "509b9488-45b9-457c-9c92-a7c851956c64";
    private const string ConfigureScheduleCommandIdText = "a0f9582f-f9af-4efa-8334-b7e8af4cba73";
    private const string EmitCommandIdText = "563f51c8-dc8c-46f0-8af4-56d2e3bf0767";
    private const string CompactGuidFormat = "N";
    private const string IdentitySeparator = "-";
    private const string CounterHexFormat = "x16";

    private const int OccurrenceIntervalDays = 1;
    [ImmutableTemporalData]
    private static readonly TimeSpan OccurrenceInterval = TimeSpan.FromDays(OccurrenceIntervalDays);
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
    internal static readonly PartitionRef Partition = new(FixtureTenant, FixtureDatabase, MessagingDomain, PartitionId);
    internal static readonly Guid ConfigureQueueCommandId = Guid.Parse(ConfigureQueueCommandIdText);
    internal static readonly Guid ConfigureCommandId = Guid.Parse(ConfigureScheduleCommandIdText);
    internal static readonly Guid EmitCommandId = Guid.Parse(EmitCommandIdText);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int DueOffsetMinutes = -1;

        var database = CrashDatabase.Create(store, tenantId: Partition.TenantId);
        var lane = new QueueLaneRef(Partition, Queue);
        ConfigureQueue(database);
        var firstDue = TimeProvider.System.GetUtcNow().AddMinutes(DueOffsetMinutes);
        ConfigureSchedule(database, lane, firstDue);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, Partition).Head.Tail;
        var operation = CreateEmitOperation(lane);
        await SaveOperationAsync(directory, tail, operation);
        ArmBoundary(store, boundary);
        _ = database.Apply(operation).Get<CommitReceipt>();
        await CrashHostPause.WaitForKillAsync();
    }

    internal static string OccurrenceId(long generation, long ordinal)
        => string.Concat(MessagePrefix, Guid.Parse(ScheduleIdText).ToString(CompactGuidFormat), IdentitySeparator,
            generation.ToString(CounterHexFormat, CultureInfo.InvariantCulture), IdentitySeparator, ordinal.ToString(CounterHexFormat, CultureInfo.InvariantCulture));

    private static void ConfigureQueue(DatabaseEngine database)
    {
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId);
        var configure = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource);
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, configure, ConfigureQueueCommandId)
            .Get<ResourceDefinition>();
    }

    private static void ConfigureSchedule(DatabaseEngine database, QueueLaneRef lane, DateTimeOffset firstDue)
    {
        const string UtcTimeZone = "UTC";
        const int NoScheduleRevision = 0;

        var definition = new RecurringScheduleDefinition(lane, Guid.Parse(ScheduleIdText), firstDue,
            OccurrenceInterval, UtcTimeZone, RecurringMisfirePolicy.CatchUp, MessagePayload, MessageHeaders);
        var command = new CommandRequest(ConfigureCommandId, Partition, [new ConfigureRecurringSchedule(definition, NoScheduleRevision)]);
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
        const int PositionStep = 1;

        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
    }
}
