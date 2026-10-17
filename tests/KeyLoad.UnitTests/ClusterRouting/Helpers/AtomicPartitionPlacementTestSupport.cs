using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementTestSupport
{
    private const string PartitionKey = "partition";
    internal static readonly PartitionRef First = new("tenant-a", "db", "domain", PartitionKey);
    internal static readonly PartitionRef SameSuffixOtherTenant = new("tenant-b", "db", "domain", PartitionKey);
    internal const int MaximumAssignments = 4096;
    internal static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    internal static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    internal static void Bootstrap(PhysicalShardCatalogFixture fixture)
        => fixture.Bootstrap(PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation,
            PhysicalShardCatalogVoterIds.Standard));

    internal static OperationResult Bind(PhysicalShardCatalogFixture fixture,
        BindAtomicPartitionPlacementRequest request)
    {
        var operation = fixture.Database.CreateNativeOperation(OperationKind.BindAtomicPartitionPlacement,
            Guid.NewGuid(), PhysicalShardCatalogFixture.RootPrincipalId,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        return fixture.Database.Apply(operation);
    }

    internal static AtomicPartitionPlacementReadRequest Read(PartitionRef partition) => new(1, partition);

    internal static byte[] ReadValue(PhysicalShardCatalogFixture fixture, byte[] key)
    {
        byte[]? value = null;
        fixture.Store.Read(view => view.ReadValue(key, bytes => value = bytes.ToArray()));
        return value ?? [];
    }

    internal static void SeedAssignments(PhysicalShardCatalogFixture fixture, int count)
    {
        fixture.Store.Commit((transaction, _) =>
        {
            var defaultShard = PhysicalShardCatalogRecordSerialization.Read(transaction)!.DefaultShard;
            for (var index = 0; index < count; index++)
            {
                var partition = new PartitionRef("tenant-a", "db", "domain", $"seed-{index:D4}");
                transaction.PutRecord(RowKey(partition), new AtomicPartitionPlacementV1(1,
                    partition, defaultShard.PhysicalShardId, 1, defaultShard.Incarnation,
                    defaultShard.VoterIds, defaultShard.PlacementEpoch));
            }

            transaction.PutRecord(DirectoryKey(), new AtomicPartitionPlacementDirectoryV1(1, count, count));
            return true;
        });
    }

    internal static byte[] DirectoryKey() => AtomicPartitionPlacementSerialization.DirectoryKey();

    internal static byte[] RowKey(PartitionRef partition)
        => AtomicPartitionPlacementSerialization.RowKey(partition);
}
