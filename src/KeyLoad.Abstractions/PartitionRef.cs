using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Identifies an atomic partition by tenant, database, transaction domain, and partition key.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
/// <param name="PartitionKey">Provides the caller-visible partition key.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PartitionRef)]
public sealed record PartitionRef([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] string TransactionDomainId, [property: Orleans.Id(3)] string PartitionKey)
{
    /// <summary>Gets the stable hash of the partition identity.</summary>
    public string AtomicPartitionId => Convert.ToHexStringLower(SHA256.HashData(
        KeyCodec.Encode(TenantId, DatabaseId, TransactionDomainId, PartitionKey)));
}
