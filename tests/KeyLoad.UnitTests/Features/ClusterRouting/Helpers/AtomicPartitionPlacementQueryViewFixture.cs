using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementQueryViewFixture : IDisposable
{
    private const string PartitionKey = "customer-1";
    internal const string Collection = "query-placement-documents";
    internal const string ReaderId = "ordinary-query-reader";
    internal const string DeniedId = "non-query-reader";
    internal static readonly PartitionRef Partition = new("tenant", "database", "orders", PartitionKey);
    internal static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    internal static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    internal static readonly ImmutableArray<string> Voters = ["node-a", "node-b", "node-c"];
    private readonly TestDatabase database = new();

    internal AtomicPartitionPlacementQueryViewFixture(bool bindPartition = true)
    {
        try
        {
            Initialize(bindPartition);
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    private void Initialize(bool bindPartition)
    {
        database.Configure(Collection, ResourceKind.Collection, Partition.TransactionDomainId);
        var bootstrap = new BootstrapPhysicalShardCatalogRequest(1, 0, ShardId, Incarnation, Voters);
        var operation = database.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId), "root",
            database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(bootstrap));
        database.Database.Apply(operation).Get<PhysicalShardCatalog>();
        if (bindPartition)
        {
            database.Submit(OperationKind.BindAtomicPartitionPlacement,
                new BindAtomicPartitionPlacementRequest(1, 0, Partition, ShardId)).Get<bool>();
        }

        var reader = new PrincipalRecord(ReaderId, Partition.TenantId,
            [new(Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        var denied = new PrincipalRecord(DeniedId, Partition.TenantId, [], []);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(denied)).Get<PrincipalRecord>();
    }

    internal DatabaseEngine Engine => database.Database;

    internal bool ReaderIsClusterAdministrator => database.Store.Read(view =>
        view.GetRecord<PrincipalRecord>(KeySpace.Principal(ReaderId))!.ClusterAdministrator);

    internal AtomicPartitionPlacementResolution Query(ReadExecutionBudgetReadGrant grant,
        PartitionRef? partition = null)
        => database.Database.WithQueryView(ReaderId, partition ?? Partition, Collection,
            (view, _, _) => DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view,
                partition ?? Partition, grant));

    internal long ExpectedReadBytes(PartitionRef? partition = null)
        => database.Store.Read(view => Size(view, PhysicalShardCatalogRecordSerialization.CatalogKey())
            + Size(view, AtomicPartitionPlacementSerialization.DirectoryKey())
            + Size(view, AtomicPartitionPlacementSerialization.RowKey(partition ?? Partition)));

    internal byte[]?[] CaptureMetadata(PartitionRef? partition = null)
        => database.Store.Read(view => new byte[]?[]
        {
            view.ReadOwnedValue(PhysicalShardCatalogRecordSerialization.CatalogKey()),
            view.ReadOwnedValue(AtomicPartitionPlacementSerialization.DirectoryKey()),
            view.ReadOwnedValue(AtomicPartitionPlacementSerialization.RowKey(partition ?? Partition))
        });

    internal void ReplaceBytes(byte[] key, byte[] bytes)
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, bytes);
            return true;
        });

    internal void DeleteDirectory()
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(AtomicPartitionPlacementSerialization.DirectoryKey());
            return true;
        });

    internal static bool SameBytes(byte[]?[] expected, byte[]?[] actual)
        => expected.Length == actual.Length
            && expected.Zip(actual).All(pair => (pair.First is null && pair.Second is null)
                || pair.First is not null && pair.Second is not null && pair.First.SequenceEqual(pair.Second));

    public void Dispose() => database.Dispose();

    private static long Size(IKeyValueView view, byte[] key)
        => key.LongLength + (view.ReadOwnedValue(key)?.LongLength ?? 0);
}
