using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.ClientApi;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryMcpContractTests
{
    [Test]
    public async Task FrozenGeneratedAliasesAndFieldIdsRemainExact()
        => await PartitionQueryMcpNativeAssertions.VerifyContractsAsync();

    [Test]
    public async Task LocalCatalogMatchesExactPartitionQuerySchemaAndEffectHints()
        => await PartitionQueryMcpCatalogAssertions.VerifyAsync();

    [Test]
    public async Task PublicAndNativeRequestBodiesRoundTripTheSameTypedValues()
    {
        var request = PartitionQueryMcpTestData.Request();
        var bytes = NativeSerialization.Serialize(request);
        var native = NativeSerialization.Deserialize<PartitionQueryRequestV1>(bytes)!;
        var publicBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options);
        var publicRoundTrip = JsonSerializer.Deserialize<PartitionQueryRequestV1>(publicBytes, JsonDefaults.Options)!;
        await Assert.That(NativeSerialization.Serialize(native).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(request)),
            JsonNode.Parse(JsonDefaults.Serialize(native)))).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(request)),
            JsonNode.Parse(JsonDefaults.Serialize(publicRoundTrip)))).IsTrue();
        await Assert.That(publicRoundTrip.Partitions.SequenceEqual([PartitionQueryMcpTestData.Partition])).IsTrue();
    }

    [Test]
    public async Task PublicAndNativePageBodiesRoundTripRowsAndLeafWitnesses()
    {
        var page = PartitionQueryMcpTestData.Page();
        var bytes = NativeSerialization.Serialize(page);
        var native = NativeSerialization.Deserialize<PartitionQueryPageV1>(bytes)!;
        var publicRoundTrip = JsonSerializer.Deserialize<PartitionQueryPageV1>(
            JsonSerializer.SerializeToUtf8Bytes(page, JsonDefaults.Options), JsonDefaults.Options)!;
        await Assert.That(native.Version).IsEqualTo(1);
        await Assert.That(native.Complete).IsTrue();
        await Assert.That(native.Rows.Length).IsEqualTo(page.Rows.Length);
        await Assert.That(native.Rows[0].Reference.Partition).IsEqualTo(PartitionQueryMcpTestData.Partition);
        await Assert.That(native.Rows[0].Reference.Collection).IsEqualTo(PartitionQueryMcpTestData.Collection);
        await Assert.That(native.Rows[0].Reference.Id).IsEqualTo(PartitionQueryMcpTestData.EntityId);
        await Assert.That(native.Rows[0].Row.EntityId).IsEqualTo(PartitionQueryMcpTestData.EntityId);
        await Assert.That(native.Rows[0].Row.Revision).IsEqualTo(7);
        await Assert.That(native.Rows[0].Row.Json).IsEqualTo(PartitionQueryMcpTestData.Json);
        await Assert.That(native.Rows[0].Row.Redacted).IsFalse();
        await Assert.That(native.Rows[0].Row.RedactedFields!.Value.IsEmpty).IsTrue();
        await Assert.That(native.Leaves.Length).IsEqualTo(1);
        await Assert.That(native.Leaves[0].Partition).IsEqualTo(PartitionQueryMcpTestData.Partition);
        await Assert.That(native.Leaves[0].CutPosition).IsEqualTo(19);
        await Assert.That(native.Leaves[0].PolicyEpoch).IsEqualTo(3);
        await Assert.That(native.Leaves[0].SchemaVersion).IsEqualTo(2);
        await Assert.That(native.Leaves[0].AccessPath).IsEqualTo(PartitionQueryMcpTestData.AccessPath);
        await Assert.That(NativeSerialization.Serialize(native).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(page)),
            JsonNode.Parse(JsonDefaults.Serialize(publicRoundTrip)))).IsTrue();
    }

    [Test]
    public async Task McpReadDecoderUsesTheActualGeneratedPartitionQueryRequest()
    {
        var item = McpCanonicalTestData.Reads().Single(value =>
            value.Name == McpCatalogExpectations.QueryPartitions);
        var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
        await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
    }

    [Test]
    public async Task InvalidPublicVersionFailsBeforeStorageRead()
    {
        using var fixture = new ReadCountingZoneTreeStore();
        var valid = PartitionQueryMcpTestData.Request();
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            new QueryEngine(fixture.Database).QueryPartitions("root", valid with { Version = 2 }, PartitionQueryPublicTestSupport.ExpectedOwner));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.ReadCalls).IsEqualTo(0);
        await Assert.That(fixture.Position).IsEqualTo(position);
    }

    [Test]
    public async Task UnsupportedAstVersionFailsBeforeStorageRead()
    {
        using var fixture = new ReadCountingZoneTreeStore();
        var valid = PartitionQueryMcpTestData.Request();
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            new QueryEngine(fixture.Database).QueryPartitions("root", valid with { AstVersion = 2 }, PartitionQueryPublicTestSupport.ExpectedOwner));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.ReadCalls).IsEqualTo(0);
        await Assert.That(fixture.Position).IsEqualTo(position);
    }
}
