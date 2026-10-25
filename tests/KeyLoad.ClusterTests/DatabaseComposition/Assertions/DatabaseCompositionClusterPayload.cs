using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Independent golden for every public field of a queue graph link, in the field order the server writes.</summary>
/// <remarks>The server serializes a derived link as from, to, label and attributes, and preserves a caller's own
/// payload text exactly; this literal shape is built without serializing the production link type.</remarks>
internal static class DatabaseCompositionClusterPayload
{
    internal static string Golden(QueueGraphLink link)
        => JsonSerializer.Serialize(new
        {
            from = Reference(link.From),
            to = Reference(link.To),
            label = link.Label,
            attributesJson = link.AttributesJson
        }, JsonDefaults.Options);

    private static object Reference(EntityRef reference)
    {
        var partition = new
        {
            tenantId = reference.Partition.TenantId,
            databaseId = reference.Partition.DatabaseId,
            transactionDomainId = reference.Partition.TransactionDomainId,
            partitionKey = reference.Partition.PartitionKey,
            atomicPartitionId = reference.Partition.AtomicPartitionId
        };
        return reference.Lifetime is { } lifetime
            ? (object)new { partition, collection = reference.Collection, id = reference.Id, lifetime = new { incarnation = lifetime.Incarnation } }
            : new { partition, collection = reference.Collection, id = reference.Id };
    }
}
