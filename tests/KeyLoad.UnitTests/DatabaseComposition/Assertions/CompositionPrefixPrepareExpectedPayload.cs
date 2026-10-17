using System.Text.Json;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

/// <summary>Independent ordinal JSON for the original opaque name-only queue input, without returned values.</summary>
internal static class CompositionPrefixPrepareExpectedPayload
{
    internal static string Link(CompositionPrefixPrepareOwner owner) => JsonSerializer.Serialize(new
    {
        attributesJson = D.EmptyJson,
        from = Reference(CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First)),
        label = D.Label,
        to = Reference(CompositionPrefixPrepareSeed.Fresh(owner, D.To, D.Second))
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
