using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal sealed class PublicReadElementsTests
{
    [Test]
    [Arguments(GrainReadKind.AstQuery, PublicQueryShape.Projection)]
    [Arguments(GrainReadKind.AstQuery, PublicQueryShape.Ordering)]
    [Arguments(GrainReadKind.AstQuery, PublicQueryShape.InValues)]
    [Arguments(GrainReadKind.LiveQueryStart, PublicQueryShape.Projection)]
    [Arguments(GrainReadKind.LiveQueryRead, PublicQueryShape.Ordering)]
    public async Task PublicJsonQueryNullElementsReachTheirOwningDiagnostics(GrainReadKind kind, PublicQueryShape shape)
    {
        using var database = new TestDatabase();
        database.Configure(PublicReadFixture.Collection, ResourceKind.Collection);
        var query = PublicReadFixture.Query(database, shape);
        var json = JsonDefaults.Serialize(query);
        var decoded = JsonDefaults.Deserialize<AstQueryRequest>(json);
        var original = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .ExecuteAst(AuthorityFixture.Root, decoded));
        var payload = PublicReadFixture.Encode(query, kind);
        var request = PublicReadFixture.Signed(database, payload, kind);
        var position = database.Store.Position;
        var execution = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            PublicReadFixture.ExecuteQueryAsync(database, request)))!;
        await Assert.That(execution.Code).IsEqualTo(PublicReadFixture.Code(shape));
        await Assert.That(execution.Message).IsEqualTo(PublicReadFixture.Detail(shape));
        await Assert.That(execution.Code).IsEqualTo(original.Code);
        await Assert.That(execution.Message).IsEqualTo(original.Message);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task SignedPublicQueryNullElementDoesNotPreemptCurrentPrincipalReload()
    {
        using var database = new TestDatabase();
        var query = PublicReadFixture.Query(database, PublicQueryShape.Projection);
        var payload = PublicReadFixture.Encode(query, GrainReadKind.AstQuery);
        var request = PublicReadFixture.Signed(database, payload, GrainReadKind.AstQuery,
            AuthorityFixture.OtherPrincipal);
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            PublicReadFixture.ExecuteQueryAsync(database, request)))!;
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicJsonNullTraversalLabelsRetainSuccessfulFiltering(bool onlyNull)
    {
        using var database = new TestDatabase();
        database.Configure(PublicReadFixture.Collection, ResourceKind.Collection);
        database.Configure(PublicReadFixture.Graph, ResourceKind.Graph);
        var start = new EntityRef(database.Partition, PublicReadFixture.Collection, PublicReadFixture.Start);
        var end = start with
        {
            Id = PublicReadFixture.End
        };
        database.Commit(new PutDocument(start.Collection, start.Id, "{}"), new PutDocument(end.Collection, end.Id, "{}"),
            new UpsertEdge(PublicReadFixture.Graph, PublicReadFixture.Edge, start, end, PublicReadFixture.Label));
        ImmutableArray<string> labels = onlyNull ? [null!] : [null!, PublicReadFixture.Label];
        var query = new TraverseRequest(database.Partition, PublicReadFixture.Graph, start, Labels: labels);
        var publicValue = JsonDefaults.Deserialize<TraverseRequest>(JsonDefaults.Serialize(query));
        var original = database.Database.Traverse(AuthorityFixture.Root, publicValue.Partition, publicValue.Graph,
            publicValue.Start, labels: publicValue.Labels?.ToArray());
        var request = PublicReadFixture.Signed(database,
            PublicReadFixture.Encode(query, GrainReadKind.Traverse), GrainReadKind.Traverse);
        var principal = GrainRequestAuthority.Reload(database.Database, request.Envelope.PrincipalId!, TimeProvider.System);
        await using var owner = new RequestWorkOwner();
        object? read = null;
        await KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsBoundCoreOwner.LeaseAsync(database, owner, (lease, _, work) =>
        {
            read = GrainCoreReadCapabilities.Execute(database.Database, GrainReadKind.Traverse, principal.Id,
                request.Payload, work, lease, null);
            return Task.CompletedTask;
        }, CancellationToken.None);
        var execution = (global::KeyLoad.GraphTraversal)read!;
        await Assert.That(execution.Vertices.SequenceEqual(original.Vertices)).IsTrue();
        await Assert.That(execution.Edges.SequenceEqual(original.Edges)).IsTrue();
        await Assert.That(execution.Vertices.Length).IsEqualTo(onlyNull ? 1 : 2);
        await Assert.That(execution.Edges.Length).IsEqualTo(onlyNull ? 0 : 1);
    }
}
