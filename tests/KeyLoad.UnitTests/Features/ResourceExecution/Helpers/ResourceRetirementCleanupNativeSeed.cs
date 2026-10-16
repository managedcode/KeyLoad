using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativeSeed
{
    internal static ResourceRetirementCleanupNativeState Create(TestDatabase fixture)
    {
        var definition = new ResourceDefinition(Table, ResourceKind.Collection, fixture.Partition.TransactionDomainId)
        {
            RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Name, RelationalColumnType.Text)]),
            Indexes = [new(ResourceRetirementCleanupNativeData.Index, [NamePath], Unique: true)]
        };
        fixture.Submit(OperationKind.CreateTable, new CreateTableRequest(fixture.Partition.TenantId,
            fixture.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        var partitions = new[] { fixture.Partition, fixture.Partition with { PartitionKey = OtherPartition } };
        foreach (var partition in partitions)
        {
            var id = Guid.NewGuid();
            fixture.Submit(OperationKind.Batch, new CommandRequest(id, partition,
                [new PutDocument(Table, First, FirstJson), new PutDocument(Table, Second, SecondJson)]), id: id).Get<CommitReceipt>();
            SeedFamilies(fixture, partition);
        }
        var lifetime = fixture.Store.Read(view => ResourceLifetimeCatalog.Read(view, fixture.Partition.TenantId,
            fixture.Partition.DatabaseId, Table, definition))!;
        var original = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var prefixes = partitions.SelectMany(partition => Families.Select(family =>
            Convert.ToHexString(KeySpace.Partition(family, partition, Table)))).ToArray();
        var owned = original.Keys.Where(key => prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);
        return new(lifetime, partitions, original, owned);
    }

    private static void SeedFamilies(TestDatabase fixture, PartitionRef partition)
        => fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeySpace.Partition(VectorFamily, partition, Table, Field, Derived), Retained);
            transaction.PutRecord(KeySpace.Partition(LineageFamily, partition, Table, Field, Derived), Retained);
            transaction.PutRecord(KeySpace.Partition(Bm25Family, partition, Table, Field, Tokenizer, Principal), Retained);
            transaction.PutRecord(KeySpace.Partition(TextFamily, partition, Table, Field, Group, Generation), Retained);
            transaction.PutRecord(KeySpace.Partition(DocumentEpochFamily, partition, Table), Generation);
            transaction.PutRecord(KeySpace.Partition(VisibilityEpochFamily, partition, Table), Generation);
            transaction.PutRecord(KeySpace.Partition(DocumentFamily, partition, Other, First), Retained);
            transaction.PutRecord(KeySpace.Partition(DocumentFamily,
                partition with
                {
                    PartitionKey = UnselectedPartition
                }, Table, First), Retained);
            transaction.PutRecord(KeySpace.Partition(EdgeFamily, partition, Table, First), Retained);
            transaction.PutRecord(KeySpace.Partition(EventFamily, partition, Table, First), Retained);
            transaction.PutRecord(KeyCodec.Encode(HistorySpace, partition.PartitionKey), Retained);
            return true;
        });
}
