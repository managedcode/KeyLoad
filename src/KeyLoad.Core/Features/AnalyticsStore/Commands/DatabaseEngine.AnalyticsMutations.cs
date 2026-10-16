using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long AnalyticsAbsentRevision = 0;
    private const long AnalyticsRevisionStep = 1;
    private const long AnalyticsAbsentGeneration = 0;
    private const long AnalyticsGenerationStep = 1;
    private const string AnalyticsRevisionConflict = "The expected analytics event revision does not match.";
    private const string AnalyticsUpsertKind = "upsertAnalyticsEvents";
    private const string AnalyticsDeleteKind = "deleteAnalyticsEvents";

    internal MutationReceipt ApplyAnalyticsUpsert(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, UpsertAnalyticsEvents mutation, DateTimeOffset now)
    {
        AuthorizeAnalyticsMutation(transaction, principal, partition, mutation);
        foreach (var value in mutation.Events.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            var key = AnalyticsStoreKeys.Event(partition, mutation.Store, value.Id);
            var previous = transaction.GetRecord<AnalyticsStoredEvent>(key);
            var revision = previous?.Event.Revision ?? AnalyticsAbsentRevision;
            if (revision != mutation.ExpectedRevisions[value.Id])
            {
                throw Errors.Fail(ErrorCode.RevisionConflict, AnalyticsRevisionConflict);
            }

            var links = AnalyticsStoreLinks.Bind(this, transaction, principal, partition, value.Links);
            var row = new AnalyticsEventRecord(value.Id, value.Timestamp, value.Dimensions, value.Metrics,
                value.RowAccess ?? previous?.Event.RowAccess, links, checked(revision + AnalyticsRevisionStep));
            transaction.PutRecord(key, new AnalyticsStoredEvent(row, false, now));
        }
        return AdvanceAnalyticsGeneration(transaction, partition, mutation.Store, AnalyticsUpsertKind,
            mutation.ExpectedRevisions.Keys.Order(StringComparer.Ordinal).Last());
    }

    internal MutationReceipt ApplyAnalyticsDelete(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, DeleteAnalyticsEvents mutation, DateTimeOffset now)
    {
        AuthorizeAnalyticsMutation(transaction, principal, partition, mutation);
        foreach (var (id, expected) in mutation.ExpectedRevisions.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var key = AnalyticsStoreKeys.Event(partition, mutation.Store, id);
            var previous = transaction.GetRecord<AnalyticsStoredEvent>(key);
            if (previous is null || previous.Deleted || previous.Event.Revision != expected)
            {
                throw Errors.Fail(ErrorCode.RevisionConflict, AnalyticsRevisionConflict);
            }

            transaction.PutRecord(key, previous with
            {
                Deleted = true,
                UpdatedAt = now,
                Event = previous.Event with
                {
                    Revision = checked(expected + AnalyticsRevisionStep)
                }
            });
        }
        return AdvanceAnalyticsGeneration(transaction, partition, mutation.Store, AnalyticsDeleteKind,
            mutation.ExpectedRevisions.Keys.Order(StringComparer.Ordinal).Last());
    }

    private static MutationReceipt AdvanceAnalyticsGeneration(IAtomicTransaction transaction, PartitionRef partition,
        string store, string kind, string id)
    {
        var key = AnalyticsStoreKeys.Generation(partition, store);
        var previous = transaction.ReadOwnedValue(key);
        var generation = checked((previous is null ? AnalyticsAbsentGeneration : NativeSerialization.Deserialize<long>(previous)) + AnalyticsGenerationStep);
        transaction.PutRecord(key, generation);
        return new(kind, store, id, generation);
    }
}
