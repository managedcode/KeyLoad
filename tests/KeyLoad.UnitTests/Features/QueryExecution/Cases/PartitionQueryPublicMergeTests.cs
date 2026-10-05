using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicMergeTests
{
    [Test]
    public async Task QueryMergesEveryAuthorizedPartitionInCanonicalOrderWithoutCollapsingEqualIds()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        fixture.AddRows(fixture.First, new PartitionQuerySeed("shared", 3, "first"), new PartitionQuerySeed("first-only", 1, "a"));
        fixture.AddRows(fixture.Second, new PartitionQuerySeed("shared", 2, "second"), new PartitionQuerySeed("second-only", 0, "b"));
        var request = fixture.Request([fixture.Second, fixture.First], 4);
        var result = new QueryEngine(fixture.Database).QueryPartitions("root", request,
            fixture.ExpectedOwner);

        await Assert.That(result.Rows.Select(row => row.Row.Json).SequenceEqual(
            ["{\"label\":\"first\"}", "{\"label\":\"second\"}", "{\"label\":\"a\"}", "{\"label\":\"b\"}"])).IsTrue();
        await Assert.That(result.Rows[0].Reference.Id).IsEqualTo("shared");
        await Assert.That(result.Rows[1].Reference.Id).IsEqualTo("shared");
        await Assert.That(result.Rows[0].Reference.Partition).IsNotEqualTo(result.Rows[1].Reference.Partition);
        await Assert.That(result.Leaves.Length).IsEqualTo(2);
        await Assert.That(result.Complete).IsTrue();
    }
}
