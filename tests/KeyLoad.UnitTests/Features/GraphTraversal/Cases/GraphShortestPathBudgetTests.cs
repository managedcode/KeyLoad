using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathBudgetTests
{
    private const string Nodes = "budget-nodes";
    private const string SourceId = "source";
    private const string MiddleId = "middle";
    private const string TargetId = "target";
    private const string ForeignPartitionKey = "other-key";

    [Test]
    public async Task AcGraph008ExactVertexAndExaminedEdgeCapsSucceedAndOneLessFails()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, SourceId);
        var middle = GraphShortestPathTestSupport.Ref(Nodes, MiddleId);
        var target = GraphShortestPathTestSupport.Ref(Nodes, TargetId);
        GraphShortestPathTestSupport.PersistVertices(database, [source, middle, target]);
        GraphShortestPathReferenceEdge[] edges =
        [
            new("edge-one", source, middle, GraphShortestPathTestSupport.Walk),
            new("edge-two", middle, target, GraphShortestPathTestSupport.Walk)
        ];
        GraphShortestPathTestSupport.PersistEdges(database, edges);
        var token = TestContext.Current!.Execution.CancellationToken;

        var exact = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal,
            GraphShortestPathTestSupport.Request(database, source, target, maxDepth: 2, maxVertices: 3, maxEdges: 2),
            cancellationToken: token);
        await Assert.That(exact.Found).IsTrue();
        await Assert.That(exact.Hops).IsEqualTo(2);
        await GraphShortestPathBudgetAssertions.AssertBudgetExceeded(database, source, target, maxDepth: 2, maxVertices: 2, maxEdges: 2, token: token);
        await GraphShortestPathBudgetAssertions.AssertBudgetExceeded(database, source, target, maxDepth: 2, maxVertices: 3, maxEdges: 1, token: token);
    }

    [Test]
    public async Task AcGraph008FilteredAndHiddenEdgeCandidatesStillConsumeTheEdgeLimit()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, "acl-source");
        var hidden = GraphShortestPathTestSupport.Ref(Nodes, "acl-hidden");
        var target = GraphShortestPathTestSupport.Ref(Nodes, "acl-target");
        database.Commit(new PutDocument(Nodes, source.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)),
            new PutDocument(Nodes, hidden.Id, "{}", Access: new(GraphShortestPathTestSupport.Bob)),
            new PutDocument(Nodes, target.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)));
        GraphShortestPathReferenceEdge[] edges =
        [
            new("a-filtered", source, hidden, GraphShortestPathTestSupport.Skip),
            new("b-hidden", source, hidden, GraphShortestPathTestSupport.Walk),
            new("z-visible", source, target, GraphShortestPathTestSupport.Walk)
        ];
        GraphShortestPathTestSupport.PersistEdges(database, edges);
        GraphShortestPathTestSupport.PersistReader(database, [Nodes], owner: GraphShortestPathTestSupport.Alice,
            restrictRows: true);
        var request = GraphShortestPathTestSupport.Request(database, source, target,
            maxDepth: 1, maxVertices: 2, maxEdges: 3, labels: ImmutableArray.Create(GraphShortestPathTestSupport.Walk));
        var token = TestContext.Current!.Execution.CancellationToken;

        var position = database.Store.Position;
        var exact = database.Database.ShortestPath(GraphShortestPathTestSupport.Alice, request,
            cancellationToken: token);
        await Assert.That(exact.Found).IsTrue();
        await Assert.That(exact.Edges.Select(edge => edge.Id).SequenceEqual(["z-visible"])).IsTrue();
        var shortRequest = request with
        {
            MaxEdges = 2
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            GraphShortestPathTestSupport.Alice, shortRequest, cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task AcGraph008ReadAndResponseByteCapsAdmitExactBytesAndRejectOneLess()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, "bytes-source");
        var target = GraphShortestPathTestSupport.Ref(Nodes, "bytes-target");
        GraphShortestPathTestSupport.PersistVertices(database, [source, target]);
        var attributes = JsonSerializer.Serialize(new
        {
            payload = new string('p', 8_192)
        });
        GraphShortestPathTestSupport.PersistEdges(database,
            [new("bytes-edge", source, target, GraphShortestPathTestSupport.Walk, attributes)]);
        var position = database.Store.Position;
        var request = GraphShortestPathTestSupport.Request(database, source, target);
        var sourceEntity = GraphShortestPathTestSupport.Vertex(database, source.Collection, source.Id);
        var targetEntity = GraphShortestPathTestSupport.Vertex(database, target.Collection, target.Id);
        var requestBytes = JsonDefaults.Serialize(request).Length;
        var measuredBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var expected = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request, measuredBudget);
        await Assert.That(expected.Edges.Single().AttributesJson).IsEqualTo(attributes);
        var readBytes = measuredBudget.ReadBytes;
        var responseBytes = JsonDefaults.Serialize(expected).Length;
        var predecessorMetadata = new
        {
            Vertex = targetEntity,
            Previous = sourceEntity,
            EdgeId = "bytes-edge"
        };
        var collectionMetadata = new
        {
            Partition = sourceEntity.Partition,
            Collection = sourceEntity.Collection
        };
        var retainedMetadataBytes = GraphShortestPathBudgetAssertions.MetadataCharge(JsonDefaults.Serialize(sourceEntity).Length)
            + GraphShortestPathBudgetAssertions.MetadataCharge(JsonDefaults.Serialize(targetEntity).Length)
            + GraphShortestPathBudgetAssertions.MetadataCharge(JsonDefaults.Serialize(predecessorMetadata).Length)
            + GraphShortestPathBudgetAssertions.MetadataCharge(JsonDefaults.Serialize(collectionMetadata).Length);
        await Assert.That((long)responseBytes).IsGreaterThan(retainedMetadataBytes);
        var exactLimits = new DatabaseLimits
        {
            MaxQueryBytes = requestBytes,
            MaxQueryReadBytes = readBytes,
            MaxBatchBytes = responseBytes
        };
        var exactEngine = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(exactLimits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var exactBudget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(exactLimits));
        var exact = exactEngine.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request, exactBudget);

        await Assert.That(exact.Found).IsTrue();
        await Assert.That(exactBudget.ReadBytes).IsEqualTo(readBytes);
        await Assert.That(JsonDefaults.Serialize(exact).Length).IsEqualTo(responseBytes);
        await GraphShortestPathBudgetAssertions.AssertQueryBudgetExceeded(database, request, requestBytes - 1);
        await GraphShortestPathBudgetAssertions.AssertReadBudgetExceeded(database, request, readBytes - 1);
        await GraphShortestPathBudgetAssertions.AssertResponseBudgetExceeded(database, request, readBytes, responseBytes - 1);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task InvalidShapeVersionAndForeignEndpointUseFrozenErrorCodes()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Nodes);
        var source = GraphShortestPathTestSupport.Ref(Nodes, "valid-source");
        var target = GraphShortestPathTestSupport.Ref(Nodes, "valid-target");
        GraphShortestPathTestSupport.PersistVertices(database, [source, target]);
        var request = GraphShortestPathTestSupport.Request(database, source, target);
        var token = TestContext.Current!.Execution.CancellationToken;

        await GraphShortestPathBudgetAssertions.RequireShapeRefusalsAsync(database, request, token);
        var foreignPartition = new PartitionRef("other-tenant", "other-database", "other-domain", ForeignPartitionKey);
        await GraphShortestPathBudgetAssertions.AssertCode(database, request with
        {
            To = request.To with
            {
                Partition = foreignPartition
            }
        },
            ErrorCode.UnsupportedCapability, token);
        var exactLabels = request with
        {
            Labels = ImmutableArray.CreateRange(Enumerable.Range(0, 64).Select(index => $"label-{index}"))
        };
        var exactLabelResult = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal,
            exactLabels, cancellationToken: token);
        await Assert.That(exactLabelResult.Found).IsFalse();
    }

}
