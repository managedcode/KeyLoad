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
    {
        using var fixture = new TestDatabase(bootstrapPhysicalShardCatalog: false);
        var request = PartitionQueryMcpTestData.Request();
        PartitionQueryMcpTestData.Seed(fixture, request);
        var nativeRequest = NativeSerialization.Deserialize<PartitionQueryRequestV1>(NativeSerialization.Serialize(request));
        var page = await PartitionQueryMcpTestData.ExecuteAsync(fixture, nativeRequest);
        var nativePage = NativeSerialization.Deserialize<PartitionQueryPageV1>(NativeSerialization.Serialize(page));
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, nativePage, fixture.Store.Position);
        await PartitionQueryMcpNativeAssertions.VerifyContractsAsync();
    }

    [Test]
    public async Task LocalCatalogMatchesExactPartitionQuerySchemaAndEffectHints()
    {
        using var fixture = new TestDatabase(bootstrapPhysicalShardCatalog: false);
        var request = PartitionQueryMcpTestData.Request();
        PartitionQueryMcpTestData.Seed(fixture, request);
        _ = await PartitionQueryMcpTestData.ExecuteAsync(fixture, PartitionQueryMcpTestData.Decode(request));
        await PartitionQueryMcpCatalogAssertions.VerifyAsync();
    }

    [Test]
    public async Task PublicAndNativeRequestBodiesRoundTripTheSameTypedValues()
    {
        var request = PartitionQueryMcpTestData.Request();
        using var fixture = new TestDatabase(bootstrapPhysicalShardCatalog: false);
        PartitionQueryMcpTestData.Seed(fixture, request);
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
        var nativePage = await PartitionQueryMcpTestData.ExecuteAsync(fixture, native);
        var publicPage = await PartitionQueryMcpTestData.ExecuteAsync(fixture, publicRoundTrip);
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(nativePage)),
            JsonNode.Parse(JsonDefaults.Serialize(publicPage)))).IsTrue();
    }

    [Test]
    public async Task PublicAndNativePageBodiesRoundTripRowsAndLeafWitnesses()
    {
        using var fixture = new TestDatabase(bootstrapPhysicalShardCatalog: false);
        var request = PartitionQueryMcpTestData.Request();
        PartitionQueryMcpTestData.Seed(fixture, request);
        var page = await PartitionQueryMcpTestData.ExecuteAsync(fixture, request);
        var bytes = NativeSerialization.Serialize(page);
        var native = NativeSerialization.Deserialize<PartitionQueryPageV1>(bytes)!;
        var publicRoundTrip = JsonSerializer.Deserialize<PartitionQueryPageV1>(
            JsonSerializer.SerializeToUtf8Bytes(page, JsonDefaults.Options), JsonDefaults.Options)!;
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, native, fixture.Store.Position);
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, publicRoundTrip, fixture.Store.Position);
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
        var request = (PartitionQueryRequestV1)decoded;
        using var fixture = new TestDatabase(bootstrapPhysicalShardCatalog: false);
        PartitionQueryMcpTestData.Seed(fixture, request, McpCanonicalTestData.Entity);
        var position = fixture.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions("root", request, PartitionQueryPublicTestSupport.ExpectedOwner));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var allowed = item with
        {
            Request = JsonSerializer.SerializeToElement(request with { AllowFullScan = true }, JsonDefaults.Options)
        };
        var allowedRequest = (PartitionQueryRequestV1)await McpNativePayloadAssertions.DecodeCanonical(allowed);
        _ = await PartitionQueryMcpTestData.ExecuteAsync(fixture, allowedRequest, McpCanonicalTestData.Entity);
    }

    [Test]
    public async Task InvalidPublicVersionFailsBeforeStorageRead()
    {
        using var fixture = new ReadCountingZoneTreeStore();
        var valid = PartitionQueryMcpTestData.Request();
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions("root", valid with { Version = 2 }, PartitionQueryPublicTestSupport.ExpectedOwner));
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
            new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions("root", valid with { AstVersion = 2 }, PartitionQueryPublicTestSupport.ExpectedOwner));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.ReadCalls).IsEqualTo(0);
        await Assert.That(fixture.Position).IsEqualTo(position);
    }
}
