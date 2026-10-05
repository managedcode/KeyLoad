using System.Globalization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class NativeSagaTimeoutJobContract
{
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

    internal static IReadOnlyDictionary<string, string> Create(DueWorkHint hint)
        => new Dictionary<string, string>(11, StringComparer.Ordinal)
        {
            [SchemaKey] = SchemaVersion,
            [KindKey] = SagaKind,
            [LaneTenantKey] = hint.Lane.Partition.TenantId,
            [LaneDatabaseKey] = hint.Lane.Partition.DatabaseId,
            [LaneTransactionDomainKey] = hint.Lane.Partition.TransactionDomainId,
            [LanePartitionKey] = hint.Lane.Partition.PartitionKey,
            [LaneQueueKey] = hint.Lane.Queue,
            [IdKey] = hint.Id.ToString("N", CultureInfo.InvariantCulture),
            [CreatorKey] = hint.CreatorPrincipalId,
            [RevisionKey] = hint.Revision.ToString(CultureInfo.InvariantCulture),
            [DeadlineKey] = hint.DueAt.ToString("O", CultureInfo.InvariantCulture)
        };

    internal static DueWorkHint Parse(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count != 11
            || !metadata.TryGetValue(SchemaKey, out var schema) || schema != SchemaVersion
            || !metadata.TryGetValue(KindKey, out var kind) || kind != SagaKind
            || !TryValue(metadata, LaneTenantKey, out var tenant)
            || !TryValue(metadata, LaneDatabaseKey, out var database)
            || !TryValue(metadata, LaneTransactionDomainKey, out var transactionDomain)
            || !TryValue(metadata, LanePartitionKey, out var partitionKey)
            || !TryValue(metadata, LaneQueueKey, out var queue)
            || !metadata.TryGetValue(IdKey, out var idText)
            || !Guid.TryParseExact(idText, "N", out var id) || id == Guid.Empty
            || !TryValue(metadata, CreatorKey, out var creator)
            || !metadata.TryGetValue(RevisionKey, out var revisionText)
            || !long.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || !metadata.TryGetValue(DeadlineKey, out var deadlineText)
            || !DateTimeOffset.TryParseExact(deadlineText, "O", CultureInfo.InvariantCulture,
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
}
