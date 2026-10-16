using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateDocumentsState(EntityRef Independent, EntityRef Related, int Iteration)
{
    private const string DocumentIdKey = "docId";
    private const string KindKey = "kind";
    private const string IterationKey = "iteration";
    private const string StatusKey = "status";
    private const string AnchorKey = "anchor";
    private const string RelatedKey = "related";
    private const string CreatedStatus = "created";

    internal const int CollectionCount = 3;
    internal const string AnchorJson = "{\"id\":\"anchor\",\"docId\":\"anchor\",\"kind\":\"anchor\",\"name\":\"functional-gate\"}";

    internal static string Collection(int index) => "gate_docs_" + index.ToString(CultureInfo.InvariantCulture);

    internal static FunctionalGateDocumentsState Create(PartitionRef partition, int iteration)
    {
        var prefix = "gate-doc-" + iteration.ToString(CultureInfo.InvariantCulture);
        return new(new(partition, Collection(1 + (iteration % 2)), prefix + "-independent"),
            new(partition, Collection(1 + (((iteration % 2) + 1) % 2)), prefix + "-related"), iteration);
    }

    internal string IndependentJson(EntityRef anchor, string status, bool linked)
        => Document(Independent.Id, "independent", status, anchor, Related, linked, canonical: status != CreatedStatus);

    internal string RelatedJson(EntityRef anchor, bool linked)
        => Document(Related.Id, "related", CreatedStatus, anchor, Independent, linked, canonical: !linked);

    private string Document(string id, string kind, string status, EntityRef anchor, EntityRef related, bool linked, bool canonical)
    {
        IDictionary<string, object?> fields = canonical
            ? new SortedDictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        fields.Add(DocumentIdKey, id);
        fields.Add(KindKey, kind);
        fields.Add(IterationKey, Iteration);
        fields.Add(StatusKey, status);
        if (linked)
        {
            fields.Add(AnchorKey, canonical ? CanonicalReference(anchor) : anchor);
            fields.Add(RelatedKey, canonical ? CanonicalReference(related) : related);
        }
        return JsonSerializer.Serialize(fields, JsonDefaults.Options);
    }

    private static object CanonicalReference(EntityRef reference) => new
    {
        collection = reference.Collection,
        id = reference.Id,
        partition = new
        {
            atomicPartitionId = reference.Partition.AtomicPartitionId,
            databaseId = reference.Partition.DatabaseId,
            partitionKey = reference.Partition.PartitionKey,
            tenantId = reference.Partition.TenantId,
            transactionDomainId = reference.Partition.TransactionDomainId
        }
    };
}
