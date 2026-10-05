using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathAuthorizationTests
{
    private const string Orders = "path-orders";
    private const string Projects = "path-projects";
    private const string SecretReadGrant = "graph.secret.read";
    private const string LabelJsonPath = "/label";
    private const string SecretJsonPath = "/secret";
    private const string ProtectedValue = "must-not-project";
    private const string VisibleValue = "visible-edge-value";

    [Test]
    public async Task AcGraph007OnlyNonemptyLabelsRequireUseAndAllLabelsProjectTheSamePath()
    {
        var policies = new[]
        {
            new SensitiveFieldPolicy(LabelJsonPath, "graph-label", RawUseGrant: GraphShortestPathTestSupport.FieldUseGrant),
            new SensitiveFieldPolicy(SecretJsonPath, "private", RawReadGrant: SecretReadGrant)
        };
        using var database = GraphShortestPathTestSupport.CreateDatabase(policies, Orders);
        var start = GraphShortestPathTestSupport.Ref(Orders, "secure-start");
        var target = GraphShortestPathTestSupport.Ref(Orders, "secure-target");
        GraphShortestPathTestSupport.PersistVertices(database, [start, target], GraphShortestPathTestSupport.Alice);
        GraphShortestPathTestSupport.PersistEdges(database,
        [
            new("secure-edge", start, target, GraphShortestPathTestSupport.Walk,
                "{\"secret\":\"must-not-project\",\"public\":\"visible-edge-value\"}")
        ]);
        GraphShortestPathTestSupport.PersistReader(database, [Orders], owner: GraphShortestPathTestSupport.Alice,
            restrictRows: true);
        var noLabelConstraint = GraphShortestPathTestSupport.Request(database, start, target);
        var emptyLabels = GraphShortestPathTestSupport.Request(database, start, target,
            labels: System.Collections.Immutable.ImmutableArray<string>.Empty);
        var token = TestContext.Current!.Execution.CancellationToken;
        var unrestricted = database.Database.ShortestPath(GraphShortestPathTestSupport.Alice, noLabelConstraint,
            cancellationToken: token);
        var empty = database.Database.ShortestPath(GraphShortestPathTestSupport.Alice, emptyLabels,
            cancellationToken: token);

        await AssertSameUnrestrictedPath(unrestricted, empty);

        var filtered = GraphShortestPathTestSupport.Request(database, start, target,
            labels: [GraphShortestPathTestSupport.Walk]);
        var result = await AssertLabelsRequireGrantAsync(database, filtered, token);

        await AssertSameUnrestrictedPath(unrestricted, result);
    }

    private static async Task<GraphShortestPathResult> AssertLabelsRequireGrantAsync(TestDatabase database,
        GraphShortestPathRequest filtered, CancellationToken token)
    {
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            GraphShortestPathTestSupport.Alice, filtered, cancellationToken: token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        GraphShortestPathTestSupport.PersistReader(database, [Orders], [GraphShortestPathTestSupport.FieldUseGrant],
            GraphShortestPathTestSupport.Alice, restrictRows: true);
        return database.Database.ShortestPath(GraphShortestPathTestSupport.Alice, filtered,
            cancellationToken: token);
    }

    [Test]
    public async Task AcGraph007MissingHiddenAndDeletedSourcesFailClosedWhileTargetsReturnNoPath()
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, Orders, Projects, "denied-targets");
        var visible = GraphShortestPathTestSupport.Ref(Orders, "visible-source");
        var hiddenSource = GraphShortestPathTestSupport.Ref(Orders, "hidden-source");
        var deletedSource = GraphShortestPathTestSupport.Ref(Orders, "deleted-source");
        var hiddenTarget = GraphShortestPathTestSupport.Ref(Orders, "hidden-target");
        var deletedTarget = GraphShortestPathTestSupport.Ref(Orders, "deleted-target");
        var deniedTarget = GraphShortestPathTestSupport.Ref("denied-targets", "denied-target");
        var middle = GraphShortestPathTestSupport.Ref(Projects, "hidden-middle");
        var ownedTarget = GraphShortestPathTestSupport.Ref(Orders, "owned-target");
        database.Commit(
            new PutDocument(Orders, visible.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)),
            new PutDocument(Orders, hiddenSource.Id, "{}", Access: new(GraphShortestPathTestSupport.Bob)),
            new PutDocument(Orders, deletedSource.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)),
            new PutDocument(Orders, hiddenTarget.Id, "{}", Access: new(GraphShortestPathTestSupport.Bob)),
            new PutDocument(Orders, deletedTarget.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)),
            new PutDocument("denied-targets", deniedTarget.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)),
            new PutDocument(Projects, middle.Id, "{}", Access: new(GraphShortestPathTestSupport.Bob)),
            new PutDocument(Orders, ownedTarget.Id, "{}", Access: new(GraphShortestPathTestSupport.Alice)));
        GraphShortestPathTestSupport.PersistEdges(database,
        [
            new("to-hidden-target", visible, hiddenTarget, GraphShortestPathTestSupport.Walk),
            new("to-deleted-target", visible, deletedTarget, GraphShortestPathTestSupport.Walk),
            new("to-denied-target", visible, deniedTarget, GraphShortestPathTestSupport.Walk),
            new("to-hidden-middle", visible, middle, GraphShortestPathTestSupport.Walk),
            new("from-hidden-middle", middle, ownedTarget, GraphShortestPathTestSupport.Walk)
        ]);
        database.Commit(new DeleteDocument(Orders, deletedSource.Id), new DeleteDocument(Orders, deletedTarget.Id));
        PersistReaderWithoutGraph(database, [Orders, Projects]);
        var token = TestContext.Current!.Execution.CancellationToken;
        await AssertGraphDeniedAsync(database, visible, ownedTarget, token);
        GraphShortestPathTestSupport.PersistReader(database, [Orders, Projects], owner: GraphShortestPathTestSupport.Alice,
            restrictRows: true);

        await AssertSourceUnavailable(database, GraphShortestPathTestSupport.Ref(Orders, "missing-source"), visible,
            ErrorCode.NotFound, token);
        await AssertSourceUnavailable(database, hiddenSource, visible, ErrorCode.NotFound, token);
        await AssertSourceUnavailable(database, deletedSource, visible, ErrorCode.NotFound, token);
        await AssertSourceUnavailable(database, GraphShortestPathTestSupport.Ref(Projects, "denied-source"), visible,
            ErrorCode.PermissionDenied, token, [Orders]);
        GraphShortestPathTestSupport.PersistReader(database, [Orders, Projects], owner: GraphShortestPathTestSupport.Alice,
            restrictRows: true);
        await AssertNoPath(database, visible, GraphShortestPathTestSupport.Ref(Orders, "missing-target"), token);
        await AssertNoPath(database, visible, hiddenTarget, token);
        await AssertNoPath(database, visible, deletedTarget, token);
        await AssertNoPath(database, visible, deniedTarget, token);
        await AssertNoPath(database, visible, ownedTarget, token);
    }

    private static async Task AssertGraphDeniedAsync(TestDatabase database,
        GraphShortestPathReferenceVertex source, GraphShortestPathReferenceVertex target, CancellationToken token)
    {
        var graphDenied = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            GraphShortestPathTestSupport.Alice, GraphShortestPathTestSupport.Request(database, source, target),
            cancellationToken: token));
        await Assert.That(graphDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    private static async Task AssertSameUnrestrictedPath(GraphShortestPathResult withoutLabels,
        GraphShortestPathResult actual)
    {
        await Assert.That(withoutLabels.Found).IsTrue();
        await Assert.That(actual.Found).IsTrue();
        await Assert.That(withoutLabels.Hops).IsEqualTo(1);
        await Assert.That(actual.Hops).IsEqualTo(withoutLabels.Hops);
        await Assert.That(actual.Vertices.SequenceEqual(withoutLabels.Vertices)).IsTrue();
        await Assert.That(withoutLabels.Edges).HasSingleItem();
        await Assert.That(actual.Edges).HasSingleItem();
        await Assert.That(actual.Edges[0].Id).IsEqualTo(withoutLabels.Edges[0].Id);
        await Assert.That(actual.Edges[0].AttributesJson)
            .IsEqualTo(withoutLabels.Edges[0].AttributesJson);
        await Assert.That(withoutLabels.Edges[0].AttributesJson.Contains(ProtectedValue, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(withoutLabels.Edges[0].AttributesJson.Contains(VisibleValue, StringComparison.Ordinal))
            .IsTrue();
    }

    private static async Task AssertSourceUnavailable(TestDatabase database,
        GraphShortestPathReferenceVertex from, GraphShortestPathReferenceVertex to, ErrorCode expectedCode,
        CancellationToken token,
        string[]? allowedCollections = null)
    {
        if (allowedCollections is not null)
        {
            GraphShortestPathTestSupport.PersistReader(database, allowedCollections,
                owner: GraphShortestPathTestSupport.Alice, restrictRows: true);
        }
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ShortestPath(
            GraphShortestPathTestSupport.Alice, GraphShortestPathTestSupport.Request(database, from, to),
            cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
    }

    private static void PersistReaderWithoutGraph(TestDatabase database, string[] collections)
    {
        var grants = collections.Select(collection => new ScopeGrant(GraphShortestPathTestSupport.DatabaseScope,
            collection, Capability.DocumentsRead)).ToImmutableArray();
        _ = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(new PrincipalRecord(GraphShortestPathTestSupport.Alice,
                GraphShortestPathTestSupport.Tenant, grants, []))).Get<PrincipalRecord>();
    }

    private static async Task AssertNoPath(TestDatabase database,
        GraphShortestPathReferenceVertex from, GraphShortestPathReferenceVertex to, CancellationToken token)
    {
        var result = database.Database.ShortestPath(GraphShortestPathTestSupport.Alice,
            GraphShortestPathTestSupport.Request(database, from, to), cancellationToken: token);
        await Assert.That(result.Found).IsFalse();
        await Assert.That(result.Hops).IsNull();
        await Assert.That(result.Vertices).IsEmpty();
        await Assert.That(result.Edges).IsEmpty();
    }
}
