using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal static class AnalyticsCommandIdentityData
{
    internal const string Root = "root";
    internal const string Store = "identity-analytics";
    internal const string Collection = "identity-documents";
    internal const string Document = "mixed";
    internal const string Json = "{\"z\":1,\"a\":2}";
    internal const string First = "a";
    internal const string Last = "z";
    private const string CanonicalTimestamp = "2024-01-01T00:01:00+00:00";
    internal static readonly DateTimeOffset From = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static AnalyticsEventData Event(string id, bool reverse, bool changed = false)
    {
        var dimensions = Map(reverse, (First, "alpha"), (Last, "omega"));
        var metrics = Map(reverse, (First, changed ? 9d : 2d), (Last, 4d));
        return new(id, From.AddMinutes(1), dimensions, metrics, null, []);
    }

    internal static ImmutableDictionary<string, T> Map<T>(bool reverse, params (string Key, T Value)[] values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var value in reverse ? values.Reverse() : values)
        {
            builder.Add(value.Key, value.Value);
        }
        return builder.ToImmutable();
    }

    internal static Mutation Analytics(bool delete, bool reverse, bool changed = false)
        => delete
            ? new DeleteAnalyticsEvents(Store, Map(reverse, (First, changed ? 2L : 1L), (Last, 1L)))
            : new UpsertAnalyticsEvents(Store, [Event(First, reverse, changed), Event(Last, reverse)],
                Map(reverse, (First, 0L), (Last, 0L)));

    internal static CommandRequest Request(TestDatabase db, Guid id, bool delete, bool reverse, bool changed = false)
        => new(id, db.Partition, [new PutDocument(Collection, Document, Json, 0), Analytics(delete, reverse, changed)]);

    internal static string CanonicalIdentity(Guid id, PartitionRef partition, bool delete)
    {
        var document = new
        {
            Access = (RowAccess?)null,
            Collection,
            ExpectedRevision = 0L,
            ExplicitReplacement = false,
            Id = Document,
            Json,
            Kind = "putDocument",
            Resource = Collection
        };
        object analytics = delete ? new
        {
            ExpectedRevisions = new SortedDictionary<string, long>(StringComparer.Ordinal) { [First] = 1, [Last] = 1 },
            Kind = AnalyticsStoreProtocol.DeleteKind,
            Resource = Store,
            Store
        } : new
        {
            Events = new[] { CanonicalEvent(First), CanonicalEvent(Last) },
            ExpectedRevisions = new SortedDictionary<string, long>(StringComparer.Ordinal) { [First] = 0, [Last] = 0 },
            Kind = AnalyticsStoreProtocol.UpsertKind,
            Resource = Store,
            Store
        };
        return JsonSerializer.Serialize(new
        {
            CommandId = id,
            Mutations = new[] { document, analytics },
            Partition = new
            {
                partition.AtomicPartitionId,
                partition.DatabaseId,
                partition.PartitionKey,
                partition.TenantId,
                partition.TransactionDomainId
            }
        }, JsonDefaults.Options);
    }

    private static object CanonicalEvent(string id) => new
    {
        Dimensions = new SortedDictionary<string, string>(StringComparer.Ordinal) { [First] = "alpha", [Last] = "omega" },
        Id = id,
        Links = Array.Empty<EntityRef>(),
        Metrics = new SortedDictionary<string, double>(StringComparer.Ordinal) { [First] = 2d, [Last] = 4d },
        RowAccess = (RowAccess?)null,
        Timestamp = CanonicalTimestamp
    };
}
