using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicMergeTests
{
    [Test]
    public async Task QueryMergesEveryAuthorizedPartitionInCanonicalOrderWithoutCollapsingEqualIds()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("shared", 3, "first"), new PartitionQueryPublicSeed("first-only", 1, "a"));
        fixture.AddRows(fixture.Second, new PartitionQueryPublicSeed("shared", 2, "second"), new PartitionQueryPublicSeed("second-only", 0, "b"));
        var request = PartitionQueryPublicTestSupport.Request([fixture.Second, fixture.First], 4);
        var result = new QueryEngine(fixture.Database).QueryPartitions("root", request,
            PartitionQueryPublicTestSupport.ExpectedOwner);

        await Assert.That(result.Rows.Select(row => row.Row.Json).SequenceEqual(
            ["{\"label\":\"first\"}", "{\"label\":\"second\"}", "{\"label\":\"a\"}", "{\"label\":\"b\"}"])).IsTrue();
        await Assert.That(result.Rows[0].Reference.Id).IsEqualTo("shared");
        await Assert.That(result.Rows[1].Reference.Id).IsEqualTo("shared");
        await Assert.That(result.Rows[0].Reference.Partition).IsNotEqualTo(result.Rows[1].Reference.Partition);
        await Assert.That(result.Leaves.Length).IsEqualTo(2);
        await Assert.That(result.Complete).IsTrue();
    }
}
