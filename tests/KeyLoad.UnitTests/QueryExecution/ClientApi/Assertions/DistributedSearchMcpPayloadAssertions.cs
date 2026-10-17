using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchMcpPayloadAssertions
{
    private const string Term = "independent literal";
    private const string SourcePartition = "independent-source";
    private const string DestinationPartition = "independent-destination";
    private const string Secret = "/secret";
    private const int NativeRank = 1;
    private const double Weight = 1;

    internal static DistributedSearchRequestV1 Request()
    {
        var source = new PartitionRef(McpCanonicalTestData.Tenant, McpCanonicalTestData.Database,
            McpCanonicalTestData.Domain, SourcePartition);
        var destination = new PartitionRef(McpCanonicalTestData.Tenant, McpCanonicalTestData.Database,
            McpCanonicalTestData.Domain, DestinationPartition);
        return new(DistributedSearchMcpProtocol.VersionOne, [source, destination],
            new(source, McpCanonicalTestData.Resource, TextField: McpCanonicalTestData.FieldPath, Text: Term,
                AllowedIds: [McpCanonicalTestData.Entity], Explain: true));
    }

    internal static async Task RequireAsync()
    {
        var request = Request();
        var expected = Page(request);
        var original = JsonDefaults.Serialize(expected);
        var publicDecoded = JsonSerializer.Deserialize<DistributedSearchPageV1>(original, JsonDefaults.Options)!;
        await EqualAsync(expected, publicDecoded);
        var native = NativeSerialization.Serialize(publicDecoded);
        var nativeDecoded = NativeSerialization.Deserialize<DistributedSearchPageV1>(native)!;
        await EqualAsync(expected, nativeDecoded);
        await Assert.That(JsonDefaults.Serialize(nativeDecoded).AsSpan().SequenceEqual(original)).IsTrue();
        var malformed = JsonNode.Parse(original)!.AsObject();
        malformed[McpCanonicalTestData.UnknownKey] = JsonValue.Create(McpCanonicalTestData.Principal);
        var malformedBytes = JsonSerializer.SerializeToUtf8Bytes(malformed);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DistributedSearchPageV1>(malformedBytes, JsonDefaults.Options));
        await EqualAsync(expected, JsonSerializer.Deserialize<DistributedSearchPageV1>(original, JsonDefaults.Options)!);
        await Assert.That(request.Search.Limit).IsEqualTo(DistributedSearchMcpProtocol.DefaultLimit);
    }

    private static DistributedSearchPageV1 Page(DistributedSearchRequestV1 request)
        => new(DistributedSearchMcpProtocol.VersionOne,
            [new(new(new(request.Partitions[NativeIndex], McpCanonicalTestData.Resource, McpCanonicalTestData.Entity),
                DistributedSearchMcpProtocol.Revision, DistributedSearchMcpProtocol.LiteralDocument, true, [Secret]),
                DistributedSearchMcpProtocol.RankedScore, new(DistributedSearchMcpProtocol.FusionConstant,
                    [new(SearchBranchKind.Text, NativeRank, Weight, DistributedSearchMcpProtocol.RankedScore)]))],
            [new(request.Partitions[NativeIndex], DistributedSearchMcpProtocol.Cut, DistributedSearchMcpProtocol.PolicyEpoch,
                DistributedSearchMcpProtocol.SchemaVersion, DistributedSearchMcpProtocol.AccessPath),
             new(request.Partitions[DestinationIndex], DistributedSearchMcpProtocol.Cut, DistributedSearchMcpProtocol.PolicyEpoch,
                DistributedSearchMcpProtocol.SchemaVersion, DistributedSearchMcpProtocol.AccessPath)],
            DistributedSearchMcpProtocol.LiteralEpoch, true);

    private const int NativeIndex = 0;
    private const int DestinationIndex = 1;

    private static async Task EqualAsync(DistributedSearchPageV1 expected, DistributedSearchPageV1 actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
