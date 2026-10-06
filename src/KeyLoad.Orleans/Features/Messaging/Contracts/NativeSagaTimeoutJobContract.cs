using System.Diagnostics;
using System.Globalization;
using KeyLoad.Core.Features.Messaging;
using Orleans.DurableJobs;

namespace KeyLoad.Orleans;

internal static class NativeSagaTimeoutJobContract
{
    private const int MetadataFieldCount = 11;
    private const string GuidFormat = "N";
    private const string UtcTimestampFormat = "O";
    private const string TraceParentVersion = "00";
    private const string TraceParentSeparator = "-";
    private const string UnsampledTraceFlags = "00";
    private static readonly HashSet<string> MetadataKeys = new(StringComparer.Ordinal)
    {
        SchemaKey,
        KindKey,
        LaneTenantKey,
        LaneDatabaseKey,
        LaneTransactionDomainKey,
        LanePartitionKey,
        LaneQueueKey,
        IdKey,
        CreatorKey,
        RevisionKey,
        DeadlineKey
    };
    internal const string JobName = "keyload-saga-timeout-v1";
    internal const string SchemaKey = "schema";
    internal const string SchemaVersion = "1";
    internal const string KindKey = "kind";
    internal const string SagaKind = "saga-timeout";
    internal const string LaneTenantKey = "lane.tenant";
    internal const string LaneDatabaseKey = "lane.database";
    internal const string LaneTransactionDomainKey = "lane.transaction-domain";
    internal const string LanePartitionKey = "lane.partition";
    internal const string LaneQueueKey = "lane.queue";
    internal const string IdKey = "saga.id";
    internal const string CreatorKey = "saga.creator";
    internal const string RevisionKey = "saga.revision";
    internal const string DeadlineKey = "saga.deadline";
    internal const string InvalidJob = "The native saga timeout job metadata is invalid.";
    internal const string UnresolvedOutcome = "The native saga timeout command outcome remains unresolved.";

    internal static IReadOnlyDictionary<string, string> Create(DueWorkHint hint)
        => new Dictionary<string, string>(MetadataFieldCount, StringComparer.Ordinal)
        {
            [SchemaKey] = SchemaVersion,
            [KindKey] = SagaKind,
            [LaneTenantKey] = hint.Lane.Partition.TenantId,
            [LaneDatabaseKey] = hint.Lane.Partition.DatabaseId,
            [LaneTransactionDomainKey] = hint.Lane.Partition.TransactionDomainId,
            [LanePartitionKey] = hint.Lane.Partition.PartitionKey,
            [LaneQueueKey] = hint.Lane.Queue,
            [IdKey] = hint.Id.ToString(GuidFormat, CultureInfo.InvariantCulture),
            [CreatorKey] = hint.CreatorPrincipalId,
            [RevisionKey] = hint.Revision.ToString(CultureInfo.InvariantCulture),
            [DeadlineKey] = hint.DueAt.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture)
        };

    internal static ScheduleJobRequest CreateScheduleRequest(GrainId target, DateTimeOffset dueTime,
        DueWorkHint hint)
        => new()
        {
            Target = target,
            JobName = JobName,
            DueTime = dueTime,
            Metadata = Create(hint),
            TraceParent = CreateIsolatedTraceParent(),
            TraceState = string.Empty
        };

    internal static DueWorkHint Parse(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count != MetadataFieldCount
            || metadata.Keys.Any(key => !MetadataKeys.Contains(key))
            || !metadata.TryGetValue(SchemaKey, out var schema) || schema != SchemaVersion
            || !metadata.TryGetValue(KindKey, out var kind) || kind != SagaKind
            || !TryValue(metadata, LaneTenantKey, out var tenant)
            || !TryValue(metadata, LaneDatabaseKey, out var database)
            || !TryValue(metadata, LaneTransactionDomainKey, out var transactionDomain)
            || !TryValue(metadata, LanePartitionKey, out var partitionKey)
            || !TryValue(metadata, LaneQueueKey, out var queue)
            || !metadata.TryGetValue(IdKey, out var idText)
            || !Guid.TryParseExact(idText, GuidFormat, out var id) || id == Guid.Empty
            || !TryValue(metadata, CreatorKey, out var creator)
            || !metadata.TryGetValue(RevisionKey, out var revisionText)
            || !long.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || !metadata.TryGetValue(DeadlineKey, out var deadlineText)
            || !DateTimeOffset.TryParseExact(deadlineText, UtcTimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var deadline)
            || deadline.Offset != TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidJob);
        }

        return new DueWorkHint(DueWorkKind.Saga,
            new QueueLaneRef(new PartitionRef(tenant, database, transactionDomain, partitionKey), queue),
            id, creator, revision, DueCoordinatorFields.NoGeneration, DueCoordinatorFields.FirstOrdinal, deadline);
    }

    private static bool TryValue(IReadOnlyDictionary<string, string> metadata, string key, out string value)
        => metadata.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    private static string CreateIsolatedTraceParent()
        => string.Concat(TraceParentVersion, TraceParentSeparator, ActivityTraceId.CreateRandom().ToHexString(),
            TraceParentSeparator, ActivitySpanId.CreateRandom().ToHexString(), TraceParentSeparator, UnsampledTraceFlags);
}
