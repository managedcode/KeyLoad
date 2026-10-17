using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class FullPartitionOutcomeIdentityAssertions
{
    private const string Administrator = "root";
    private const string ChangedTenant = "identity-tenant";
    private const string ChangedDatabase = "identity-database";
    private const string ChangedDomain = "identity-domain";
    private const string ChangedPartition = "identity-partition";

    internal static PartitionRef ChangedPartitionRef(PartitionRef original, string component)
        => component switch
        {
            nameof(PartitionRef.TenantId) => original with { TenantId = ChangedTenant },
            nameof(PartitionRef.DatabaseId) => original with { DatabaseId = ChangedDatabase },
            nameof(PartitionRef.TransactionDomainId) => original with { TransactionDomainId = ChangedDomain },
            nameof(PartitionRef.PartitionKey) => original with { PartitionKey = ChangedPartition },
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

    internal static async Task AssertOnlyComponentChangedAsync(PartitionRef original, PartitionRef changed,
        string component)
    {
        await Assert.That(changed.TenantId != original.TenantId).IsEqualTo(component == nameof(PartitionRef.TenantId));
        await Assert.That(changed.DatabaseId != original.DatabaseId).IsEqualTo(component == nameof(PartitionRef.DatabaseId));
        await Assert.That(changed.TransactionDomainId != original.TransactionDomainId)
            .IsEqualTo(component == nameof(PartitionRef.TransactionDomainId));
        await Assert.That(changed.PartitionKey != original.PartitionKey).IsEqualTo(component == nameof(PartitionRef.PartitionKey));
    }

    internal static async Task AssertStoredOutcomeAsync(ZoneTreeStore store, PartitionRef partition,
        Guid commandId, byte[] receiptBytes)
    {
        var key = OutcomeStoreOracle.PartitionKey(partition, Administrator, commandId);
        var stored = store.Read(view => view.GetRecord<StoredOutcome>(key));
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(partition);
        await Assert.That(NativeSerialization.Serialize(stored.Result.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(receiptBytes)).IsTrue();
    }

    internal static async Task AssertReceiptAsync(OperationResult result, byte[] expected)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(expected)).IsTrue();
    }

    internal static async Task AssertDocumentAsync(DatabaseEngine database, PartitionRef partition,
        string resource, string documentId, string expectedJson)
    {
        var document = database.GetDocument(Administrator, new(partition, resource, documentId));
        await Assert.That(document?.Json).IsEqualTo(expectedJson);
    }

    internal static async Task AssertOutboxTailsAsync(DatabaseEngine database, PartitionRef firstPartition,
        PartitionRef secondPartition, long firstTail, long secondTail)
    {
        await Assert.That(database.GetOutboxStatus(Administrator, firstPartition).Head.Tail).IsEqualTo(firstTail);
        await Assert.That(database.GetOutboxStatus(Administrator, secondPartition).Head.Tail).IsEqualTo(secondTail);
    }
}
