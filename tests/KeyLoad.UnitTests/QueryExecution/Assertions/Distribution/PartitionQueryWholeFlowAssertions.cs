using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryWholeFlowAssertions
{
    internal static async Task AssertPublicRowAsync(PartitionQueryPage page, PartitionRef partition,
        string id, string label, ResourceLifetime creation)
    {
        await Assert.That(page.Version).IsEqualTo(1);
        await Assert.That(page.Complete).IsTrue();
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await Assert.That(page.Rows[0].Reference)
            .IsEqualTo(PartitionQueryCreationOracle.Reference(creation, partition, id));
        await Assert.That(page.Rows[0].Row.Json).IsEqualTo("{\"label\":\"" + label + "\"}");
        await Assert.That(page.Rows[0].Row.Revision).IsEqualTo(1L);
        await Assert.That(page.Leaves.Length).IsEqualTo(1);
        await Assert.That(page.Leaves[0].Partition).IsEqualTo(partition);
        await Assert.That(page.Leaves[0].CutPosition).IsGreaterThan(0);
        await Assert.That(page.Leaves[0].PolicyEpoch).IsGreaterThan(0);
    }

    internal static async Task RuntimeAssertRowAsync(PartitionQueryResult result, PartitionRef partition,
        string id, string label, ResourceLifetime creation)
    {
        await Assert.That(result.Complete).IsTrue();
        await Assert.That(result.Rows.Length).IsEqualTo(1);
        await Assert.That(result.Rows[0].Reference)
            .IsEqualTo(PartitionQueryCreationOracle.Reference(creation, partition, id));
        await Assert.That(result.Rows[0].Row.Json).IsEqualTo("{\"label\":\"" + label + "\"}");
        await Assert.That(result.Rows[0].Row.Revision).IsEqualTo(1L);
        await Assert.That(result.Leaves.Length).IsEqualTo(1);
        await Assert.That(result.Leaves[0].Partition).IsEqualTo(partition);
        await Assert.That(result.Leaves[0].CutPosition).IsGreaterThan(0);
    }
}
