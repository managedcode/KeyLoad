using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Identifies an atomic partition by tenant, database, transaction domain, and partition key.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
/// <param name="PartitionKey">Provides the caller-visible partition key.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PartitionRef)]
public sealed record PartitionRef([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] string TransactionDomainId, [property: Orleans.Id(3)] string PartitionKey)
{
    [NonSerialized]
    private string? atomicPartitionId;

    /// <summary>Gets the stable hash of the partition identity, computed once for this immutable reference.</summary>
    public string AtomicPartitionId => atomicPartitionId ??= Convert.ToHexStringLower(SHA256.HashData(
        KeyCodec.Encode(TenantId, DatabaseId, TransactionDomainId, PartitionKey)));

    /// <summary>Equality covers the four identity components only, never the retained hash.</summary>
    public bool Equals(PartitionRef? other) => other is not null && (ReferenceEquals(this, other)
        || string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
        && string.Equals(DatabaseId, other.DatabaseId, StringComparison.Ordinal)
        && string.Equals(TransactionDomainId, other.TransactionDomainId, StringComparison.Ordinal)
        && string.Equals(PartitionKey, other.PartitionKey, StringComparison.Ordinal));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
}
