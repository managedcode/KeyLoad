using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Independent ordinal-key golden for every public field of the RF3 link fixture.</summary>
internal static class DatabaseCompositionRf3Payload
{
    internal static string OrdinalGolden(QueueGraphLink link) => JsonSerializer.Serialize(new
    {
        attributesJson = link.AttributesJson,
        from = Reference(link.From),
        label = link.Label,
        to = Reference(link.To)
    }, JsonDefaults.Options);

    private static object Reference(EntityRef reference) => new
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
