using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryAuthorizationTests
{
    [Test]
    public async Task DenialInLaterPartitionReturnsNoPartialRowsAndDoesNotChangeTheStore()
    {
        using var database = PartitionQueryTestSupport.Create();
        var foreign = database.Partition with
        {
            TenantId = PartitionQueryTestSupport.ForeignTenant,
            PartitionKey = "foreign-leaf"
        };
        PartitionQueryTestSupport.ConfigureForeignPartition(database, foreign);
        PartitionQueryTestSupport.AddRows(database, database.Partition, new PartitionQuerySeed("visible", 2, "visible"));
        PartitionQueryTestSupport.AddRows(database, foreign, new PartitionQuerySeed("denied", 1, "denied"));
        PartitionQueryTestSupport.ConfigureSinglePartitionReader(database);
        var position = database.Store.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(database.Database)
            .ExecutePartitionQuery("partition-query-reader", PartitionQueryTestSupport.Request(database),
                [database.Partition, foreign]));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = new QueryEngine(database.Database).ExecutePartitionQuery("partition-query-reader",
            PartitionQueryTestSupport.Request(database), [database.Partition]);
        await Assert.That(healthy.Rows.Select(row => row.Reference.Id).SequenceEqual(["visible"])).IsTrue();
    }
}
