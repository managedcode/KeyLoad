using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Query;
using KeyLoad.UnitTests.ClientApi;
using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class PartitionQueryMcpContractTests
{
    [Test]
    public async Task FrozenGeneratedAliasesAndFieldIdsRemainExact()
    {
        using var fixture = new TestDatabase();
        var request = PartitionQueryMcpTestData.Request();
        var creation = PartitionQueryMcpTestData.Seed(fixture, request);
        var runtimeRequest = OrleansSerialization.Deserialize<PartitionQueryRequest>(OrleansSerialization.Serialize(request));
        var page = await PartitionQueryMcpTestData.ExecuteAsync(fixture, runtimeRequest, creation);
        var runtimePage = OrleansSerialization.Deserialize<PartitionQueryPage>(OrleansSerialization.Serialize(page));
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, runtimePage, fixture.Store.Position, creation);
        await PartitionQueryMcpAssertions.VerifyContractsAsync();
    }

    [Test]
    public async Task LocalCatalogMatchesExactPartitionQuerySchemaAndEffectHints()
    {
        using var fixture = new TestDatabase();
        var request = PartitionQueryMcpTestData.Request();
        var creation = PartitionQueryMcpTestData.Seed(fixture, request);
        _ = await PartitionQueryMcpTestData.ExecuteAsync(fixture, PartitionQueryMcpTestData.Decode(request), creation);
        await PartitionQueryMcpCatalogAssertions.VerifyAsync();
    }

    [Test]
    public async Task PublicAndRequestBodiesRoundTripTheSameTypedValues()
    {
        var request = PartitionQueryMcpTestData.Request();
        using var fixture = new TestDatabase();
        var creation = PartitionQueryMcpTestData.Seed(fixture, request);
        var bytes = OrleansSerialization.Serialize(request);
        var execution = OrleansSerialization.Deserialize<PartitionQueryRequest>(bytes)!;
        var publicBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options);
        var publicRoundTrip = JsonSerializer.Deserialize<PartitionQueryRequest>(publicBytes, JsonDefaults.Options)!;
        await Assert.That(OrleansSerialization.Serialize(execution).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(request)),
            JsonNode.Parse(JsonDefaults.Serialize(execution)))).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(request)),
            JsonNode.Parse(JsonDefaults.Serialize(publicRoundTrip)))).IsTrue();
        await Assert.That(publicRoundTrip.Partitions.SequenceEqual([PartitionQueryMcpTestData.Partition])).IsTrue();
        var runtimePage = await PartitionQueryMcpTestData.ExecuteAsync(fixture, execution, creation);
        var publicPage = await PartitionQueryMcpTestData.ExecuteAsync(fixture, publicRoundTrip, creation);
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(runtimePage)),
            JsonNode.Parse(JsonDefaults.Serialize(publicPage)))).IsTrue();
    }

    [Test]
    public async Task PublicAndPageBodiesRoundTripRowsAndLeafWitnesses()
    {
        using var fixture = new TestDatabase();
        var request = PartitionQueryMcpTestData.Request();
        var creation = PartitionQueryMcpTestData.Seed(fixture, request);
        var page = await PartitionQueryMcpTestData.ExecuteAsync(fixture, request, creation);
        var bytes = OrleansSerialization.Serialize(page);
        var execution = OrleansSerialization.Deserialize<PartitionQueryPage>(bytes)!;
        var publicRoundTrip = JsonSerializer.Deserialize<PartitionQueryPage>(
            JsonSerializer.SerializeToUtf8Bytes(page, JsonDefaults.Options), JsonDefaults.Options)!;
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, execution, fixture.Store.Position, creation);
        await PartitionQueryMcpTestData.VerifyPageAsync(fixture, request, publicRoundTrip, fixture.Store.Position, creation);
        await Assert.That(OrleansSerialization.Serialize(execution).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(page)),
            JsonNode.Parse(JsonDefaults.Serialize(publicRoundTrip)))).IsTrue();
    }

    [Test]
    public async Task McpReadDecoderUsesTheActualGeneratedPartitionQueryRequest()
    {
        var item = McpCanonicalTestData.Reads().Single(value =>
            value.Name == McpCatalogExpectations.QueryPartitions);
        var decoded = await McpPayloadAssertions.DecodeCanonical(item);
        await McpPayloadAssertions.AssertWritersAgree(item, decoded);
        var request = (PartitionQueryRequest)decoded;
        using var fixture = new TestDatabase();
        var creation = PartitionQueryMcpTestData.Seed(fixture, request, McpCanonicalTestData.Entity);
        var position = fixture.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions("root", request, PartitionQueryMcpTestData.Owner(fixture.Database)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability).Because(failure.Message);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var allowed = item with
        {
            Request = JsonSerializer.SerializeToElement(request with
            {
                AllowFullScan = true
            }, JsonDefaults.Options)
        };
        var allowedRequest = (PartitionQueryRequest)await McpPayloadAssertions.DecodeCanonical(allowed);
        _ = await PartitionQueryMcpTestData.ExecuteAsync(fixture, allowedRequest, creation, McpCanonicalTestData.Entity);
    }

    [Test]
    public async Task InvalidPublicVersionFailsBeforeStorageRead()
    {
        using var fixture = new ReadCountingZoneTreeStore();
        var valid = PartitionQueryMcpTestData.Request();
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions("root", valid with
            {
                Version = 2
            }, PartitionQueryMcpTestData.Owner(fixture.Database)));
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
            new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions("root", valid with
            {
                AstVersion = 3
            }, PartitionQueryMcpTestData.Owner(fixture.Database)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.ReadCalls).IsEqualTo(0);
        await Assert.That(fixture.Position).IsEqualTo(position);
    }
}
