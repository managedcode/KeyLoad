using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphPathNativePayloadTests
{
    private const string Tenant = "native-path-tenant";
    private const string DatabaseName = "native-path-database";
    private const string Domain = "native-path-domain";
    private const string PartitionKey = "native-path-key";
    private const string Orders = "native-orders";
    private const string Projects = "native-projects";
    private const string SharedId = "same-id";
    private const string Graph = "native-path-links";
    private const string Label = "native-walk";
    private const string Attributes = "{\"public\":\"visible\"}";
    private const string Principal = "native-path-principal";
    private const string GraphParameter = "graph";
    private const string DepthParameter = "depth";
    private const string EnabledParameter = "enabled";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectRequestRoundTripsFullReferencesAndNullOrEmptyLabels(bool emptyLabels)
    {
        var partition = Partition();
        var source = new EntityRef(partition, Orders, SharedId);
        var target = new EntityRef(partition, Projects, SharedId);
        ImmutableArray<string>? labels = emptyLabels ? ImmutableArray<string>.Empty : null;
        var request = new GraphShortestPathRequest(1, partition, Graph, source, target, Labels: labels);
        var actual = NativeSerialization.Deserialize<GraphShortestPathRequest>(NativeSerialization.Serialize(request));

        await Assert.That(actual.Version).IsEqualTo(request.Version);
        await Assert.That(actual.Partition).IsEqualTo(partition);
        await Assert.That(actual.Graph).IsEqualTo(Graph);
        await Assert.That(actual.From).IsEqualTo(source);
        await Assert.That(actual.To).IsEqualTo(target);
        await Assert.That(actual.Labels.HasValue).IsEqualTo(emptyLabels);
        if (emptyLabels)
        {
            await Assert.That(actual.Labels!.Value.IsEmpty).IsTrue();
        }
    }

    [Test]
    public async Task ResultRoundTripsOrderedImmutableVerticesAndProjectedEdges()
    {
        var partition = Partition();
        var source = new EntityRef(partition, Orders, SharedId);
        var target = new EntityRef(partition, Projects, SharedId);
        var edge = new EdgeRecord("native-edge", source, target, Label, Attributes, 7);
        var result = new GraphShortestPathResult(1, true, 1, [source, target], [edge], 29);
        var actual = NativeSerialization.Deserialize<GraphShortestPathResult>(NativeSerialization.Serialize(result));

        await Assert.That(actual.Version).IsEqualTo(1);
        await Assert.That(actual.Found).IsTrue();
        await Assert.That(actual.Hops).IsEqualTo((int?)1);
        await Assert.That(actual.Vertices.SequenceEqual([source, target])).IsTrue();
        await Assert.That(actual.Edges).HasSingleItem();
        await Assert.That(actual.Edges[0]).IsEqualTo(edge);
        await Assert.That(actual.CutPosition).IsEqualTo(29L);
    }

    [Test]
    public async Task SqlRequestRoundTripsTypedParametersAndQueryScope()
    {
        var partition = Partition();
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [GraphParameter] = JsonSerializer.SerializeToElement(Graph),
            [DepthParameter] = JsonSerializer.SerializeToElement(9),
            [EnabledParameter] = JsonSerializer.SerializeToElement(true)
        };
        var query = new QueryRequest(partition, "SELECT * FROM GRAPH_SHORTEST_PATH(...) ", parameters,
            AllowFullScan: true);
        var request = new SqlGraphPathRequest(1, query);
        var actual = NativeSerialization.Deserialize<SqlGraphPathRequest>(NativeSerialization.Serialize(request));

        await Assert.That(actual.Version).IsEqualTo(1);
        await Assert.That(actual.Query.Partition).IsEqualTo(partition);
        await Assert.That(actual.Query.Sql).IsEqualTo(query.Sql);
        await Assert.That(actual.Query.AllowFullScan).IsTrue();
        await Assert.That(actual.Query.Cursor).IsNull();
        await Assert.That(actual.Query.Parameters![GraphParameter].GetString()).IsEqualTo(Graph);
        await Assert.That(actual.Query.Parameters[DepthParameter].GetInt32()).IsEqualTo(9);
        await Assert.That(actual.Query.Parameters[EnabledParameter].GetBoolean()).IsTrue();
    }

    [Test]
    public async Task PublicInputNullLabelSurvivesNativeDecodeAndGetsCanonicalValidation()
    {
        using var database = new TestDatabase();
        var request = new GraphShortestPathRequest(1, database.Partition, Graph,
            new(database.Partition, Orders, "source"), new(database.Partition, Projects, "target"),
            Labels: ImmutableArray.Create((string)null!));
        var payload = NativeSerialization.Serialize(request, NativeValidationProfile.PublicInputElements);
        var decoded = GrainNativePayload.ReadPublicInput<GraphShortestPathRequest>(payload);

        await Assert.That(decoded.Labels.HasValue).IsTrue();
        await Assert.That(decoded.Labels!.Value.Length).IsEqualTo(1);
        await Assert.That(decoded.Labels.Value[0] is null).IsTrue();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            Principal, decoded, cancellationToken: CancellationToken.None));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AppendedReadKindsPassTheSignedNativeEnvelopeValidator()
    {
        using var database = new TestDatabase();
        var codec = new GrainRequestCodec(database.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var partition = database.Partition;
        var direct = new GraphShortestPathRequest(1, partition, Graph,
            new(partition, Orders, "source"), new(partition, Projects, "target"));
        var sql = new SqlGraphPathRequest(1, new QueryRequest(partition, "SELECT * FROM GRAPH_SHORTEST_PATH(...) ",
            AllowFullScan: true));

        await AssertEnvelopeAsync(codec, GrainReadKind.GraphShortestPath, NativeSerialization.Serialize(direct));
        await AssertEnvelopeAsync(codec, GrainReadKind.SqlGraphPath, NativeSerialization.Serialize(sql));
        await Assert.That(GrainCoreReadCapabilities.Handles(GrainReadKind.GraphShortestPath)).IsTrue();
    }

    private static async Task AssertEnvelopeAsync(GrainRequestCodec codec, GrainReadKind kind,
        ReadOnlyMemory<byte> payload)
    {
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, kind, payload);
        var verified = codec.VerifyRead(token, requestId);
        await Assert.That(verified.Envelope.ReadKind).IsEqualTo(kind);
        await Assert.That(verified.Payload.Span.SequenceEqual(payload.Span)).IsTrue();
    }

    private static PartitionRef Partition() => new(Tenant, DatabaseName, Domain, PartitionKey);
}
