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
    private AtomicPartitionIdentity? atomicPartitionIdentity;

    /// <summary>Gets the stable hash of the partition identity, computed once per identity.</summary>
    /// <remarks>A <c>with</c> copy carries the retained hash of its original, so the hash is reused only while it still
    /// describes the exact identity components of this reference.</remarks>
    public string AtomicPartitionId
    {
        get
        {
            var retained = atomicPartitionIdentity;
            if (retained is not null && retained.Describes(this))
            {
                return retained.Value;
            }
            var computed = new AtomicPartitionIdentity(this);
            atomicPartitionIdentity = computed;
            return computed.Value;
        }
    }

    /// <summary>Equality covers the four identity components only, never the retained hash.</summary>
    public bool Equals(PartitionRef? other) => other is not null && (ReferenceEquals(this, other)
        || string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
        && string.Equals(DatabaseId, other.DatabaseId, StringComparison.Ordinal)
        && string.Equals(TransactionDomainId, other.TransactionDomainId, StringComparison.Ordinal)
        && string.Equals(PartitionKey, other.PartitionKey, StringComparison.Ordinal));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TenantId, DatabaseId, TransactionDomainId, PartitionKey);

    private sealed class AtomicPartitionIdentity(PartitionRef source)
    {
        private readonly string tenantId = source.TenantId;
        private readonly string databaseId = source.DatabaseId;
        private readonly string transactionDomainId = source.TransactionDomainId;
        private readonly string partitionKey = source.PartitionKey;

        internal string Value { get; } = Convert.ToHexStringLower(SHA256.HashData(
            KeyCodec.Encode(source.TenantId, source.DatabaseId, source.TransactionDomainId, source.PartitionKey)));

        internal bool Describes(PartitionRef partition)
            => ReferenceEquals(tenantId, partition.TenantId) && ReferenceEquals(databaseId, partition.DatabaseId)
                && ReferenceEquals(transactionDomainId, partition.TransactionDomainId)
                && ReferenceEquals(partitionKey, partition.PartitionKey);
    }
}
