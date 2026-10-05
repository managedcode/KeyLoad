using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicBudgetTests
{
    private const int PageDescriptorBytes = 64;
    private const int ArrayDescriptorBytes = 32;
    private const int ReferenceBytes = 8;
    private const int RowDescriptorBytes = 32;
    private const int WitnessDescriptorBytes = 64;

    [Test]
    public async Task OversizedRequestAndPublicPageRetentionFailAtTheirExactBoundaries()
    {
        using var oversizedFixture = new PartitionQueryPublicTestSupport(new() { MaxQueryBytes = 128 });
        var oversized = PartitionQueryPublicTestSupport.Request([oversizedFixture.First], 1);
        var position = oversizedFixture.Position;
        var requestFailure = Assert.ThrowsExactly<KeyLoadException>(() => Run(oversizedFixture, oversized));
        await Assert.That(requestFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(oversizedFixture.Position).IsEqualTo(position);

        var measured = MeasurePublicPage();
        var mapBytes = MappingBytes(measured.Rows.Length, measured.Leaves.Length);
        var projected = new PartitionQueryPageV1(1,
            [.. measured.Rows.Select(candidate => new PartitionQueryRowV1(candidate.Reference, candidate.Row))],
            [.. measured.Leaves.Select(leaf => new PartitionQueryLeafWitnessV1(leaf.Partition,
                leaf.CutPosition, leaf.PolicyEpoch, leaf.SchemaVersion, leaf.AccessPath))], true);
        var exactBytes = checked((int)Math.Max(measured.RetainedBytes + mapBytes,
            JsonDefaults.Serialize(projected).Length));
        using var exact = new PartitionQueryPublicTestSupport(new() { MaxBatchBytes = exactBytes });
        SeedOne(exact);
        var page = Run(exact, PartitionQueryPublicTestSupport.Request([exact.First], 1));
        await Assert.That(page.Complete).IsTrue();

        using var below = new PartitionQueryPublicTestSupport(new() { MaxBatchBytes = exactBytes - 1 });
        SeedOne(below);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Run(below, PartitionQueryPublicTestSupport.Request([below.First], 1)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static PartitionQueryResultV1 MeasurePublicPage()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        SeedOne(fixture);
        var query = PartitionQueryPublicTestSupport.Query(1);
        var request = QueryValidation.Normalize(new AstQueryRequest(fixture.First, query, AllowFullScan: true),
            fixture.Database.Limits);
        return new QueryEngine(fixture.Database).ExecutePartitionQuery("root", request, [fixture.First]);
    }

    private static long MappingBytes(int rows, int leaves)
    {
        var rowStorage = ArrayDescriptorBytes + (long)ReferenceBytes * rows + (long)RowDescriptorBytes * rows;
        var leafStorage = ArrayDescriptorBytes + (long)ReferenceBytes * leaves + (long)WitnessDescriptorBytes * leaves;
        return PageDescriptorBytes + rowStorage + leafStorage;
    }

    private static void SeedOne(PartitionQueryPublicTestSupport fixture)
        => fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("budget-row", 1, "one"));

    private static PartitionQueryPageV1 Run(PartitionQueryPublicTestSupport fixture,
        PartitionQueryRequestV1 request)
        => new QueryEngine(fixture.Database).QueryPartitions("root", request,
            PartitionQueryPublicTestSupport.ExpectedOwner);
}
