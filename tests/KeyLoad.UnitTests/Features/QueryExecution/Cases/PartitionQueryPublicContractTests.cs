using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicContractTests
{
    [Test]
    public async Task GeneratedPublicPageRoundTripsFullReferencesAndPerLeafWitnesses()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("same", 2, "first"));
        fixture.AddRows(fixture.Second, new PartitionQueryPublicSeed("same", 1, "second"));
        var request = PartitionQueryPublicTestSupport.Request([fixture.Second, fixture.First], 2);
        var result = Run(fixture, request);
        var bytes = NativeSerialization.Serialize(result);
        var roundTrip = NativeSerialization.Deserialize<PartitionQueryPageV1>(bytes);

        await Assert.That(roundTrip.Version).IsEqualTo(1);
        await Assert.That(roundTrip.Complete).IsTrue();
        await Assert.That(roundTrip.Rows.Select(row => row.Reference.Id).SequenceEqual(["same", "same"])).IsTrue();
        await Assert.That(roundTrip.Rows[0].Reference.Partition).IsEqualTo(fixture.First);
        await Assert.That(roundTrip.Rows[1].Reference.Partition).IsEqualTo(fixture.Second);
        await Assert.That(roundTrip.Leaves.Select(leaf => leaf.Partition).SequenceEqual([fixture.First, fixture.Second])).IsTrue();
        await Assert.That(roundTrip.Leaves.All(leaf => leaf.CutPosition > 0 && leaf.PolicyEpoch > 0
            && leaf.SchemaVersion > 0 && leaf.AccessPath.Length > 0)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(roundTrip).SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task DefaultEmptyOversizedDuplicateAndUnsupportedRequestsFailBeforeStorageRead()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("valid-leaf", 1, "healthy"));
        var valid = PartitionQueryPublicTestSupport.Request([fixture.First], 1);
        var position = fixture.Position;
        var empty = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture, valid with { Partitions = [] }));
        var defaultArray = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture,
            valid with { Partitions = default }));
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture,
            valid with { Partitions = [fixture.First, fixture.First] }));
        var oversizedParts = Enumerable.Range(0, 9).Select(index => fixture.First with
        { PartitionKey = "query-partition-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .ToImmutableArray();
        var oversized = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture,
            valid with { Partitions = oversizedParts }));
        var unsupported = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture,
            valid with { Query = valid.Query with { Explain = true } }));
        var badAst = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture, valid with { AstVersion = 2 }));
        var badVersion = Assert.ThrowsExactly<KeyLoadException>(() => Run(fixture, valid with { Version = 2 }));

        await Assert.That(empty.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(defaultArray.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(oversized.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(unsupported.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(badAst.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(badVersion.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Position).IsEqualTo(position);
        var healthy = Run(fixture, valid);
        await PartitionQueryWholeFlowAssertions.AssertPublicRowAsync(healthy, fixture.First, "valid-leaf", "healthy");
        await Assert.That(fixture.Position).IsEqualTo(position);
    }

    private static PartitionQueryPageV1 Run(PartitionQueryPublicTestSupport fixture,
        PartitionQueryRequestV1 request, string principalId = "root")
        => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(principalId, request,
            PartitionQueryPublicTestSupport.ExpectedOwner);
}
