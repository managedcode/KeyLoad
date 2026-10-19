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
    private const int RuntimeRank = 1;
    private const double Weight = 1;

    internal static DistributedSearchRequest Request()
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
        var publicDecoded = JsonSerializer.Deserialize<DistributedSearchPage>(original, JsonDefaults.Options)!;
        await EqualAsync(expected, publicDecoded);
        var execution = OrleansSerialization.Serialize(publicDecoded);
        var runtimeDecoded = OrleansSerialization.Deserialize<DistributedSearchPage>(execution)!;
        await EqualAsync(expected, runtimeDecoded);
        await Assert.That(JsonDefaults.Serialize(runtimeDecoded).AsSpan().SequenceEqual(original)).IsTrue();
        var malformed = JsonNode.Parse(original)!.AsObject();
        malformed[McpCanonicalTestData.UnknownKey] = JsonValue.Create(McpCanonicalTestData.Principal);
        var malformedBytes = JsonSerializer.SerializeToUtf8Bytes(malformed);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DistributedSearchPage>(malformedBytes, JsonDefaults.Options));
        await EqualAsync(expected, JsonSerializer.Deserialize<DistributedSearchPage>(original, JsonDefaults.Options)!);
        await Assert.That(request.Search.Limit).IsEqualTo(DistributedSearchMcpProtocol.DefaultLimit);
    }

    private static DistributedSearchPage Page(DistributedSearchRequest request)
        => new(DistributedSearchMcpProtocol.VersionOne,
            [new(new(new(request.Partitions[RuntimeIndex], McpCanonicalTestData.Resource, McpCanonicalTestData.Entity),
                DistributedSearchMcpProtocol.Revision, DistributedSearchMcpProtocol.LiteralDocument, true, [Secret]),
                DistributedSearchMcpProtocol.RankedScore, new(DistributedSearchMcpProtocol.FusionConstant,
                    [new(SearchBranchKind.Text, RuntimeRank, Weight, DistributedSearchMcpProtocol.RankedScore)]))],
            [new(request.Partitions[RuntimeIndex], DistributedSearchMcpProtocol.Cut, DistributedSearchMcpProtocol.PolicyEpoch,
                DistributedSearchMcpProtocol.SchemaVersion, DistributedSearchMcpProtocol.AccessPath),
             new(request.Partitions[DestinationIndex], DistributedSearchMcpProtocol.Cut, DistributedSearchMcpProtocol.PolicyEpoch,
                DistributedSearchMcpProtocol.SchemaVersion, DistributedSearchMcpProtocol.AccessPath)],
            DistributedSearchMcpProtocol.LiteralEpoch, true);

    private const int RuntimeIndex = 0;
    private const int DestinationIndex = 1;

    private static async Task EqualAsync(DistributedSearchPage expected, DistributedSearchPage actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
