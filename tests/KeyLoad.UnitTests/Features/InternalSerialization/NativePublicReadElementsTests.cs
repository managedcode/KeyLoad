using System.Collections.Immutable;
using KeyLoad.Orleans;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativePublicReadElementsTests
{
    [Test]
    [Arguments(GrainReadKind.AstQuery, NativePublicQueryShape.Projection)]
    [Arguments(GrainReadKind.AstQuery, NativePublicQueryShape.Ordering)]
    [Arguments(GrainReadKind.AstQuery, NativePublicQueryShape.InValues)]
    [Arguments(GrainReadKind.LiveQueryStart, NativePublicQueryShape.Projection)]
    [Arguments(GrainReadKind.LiveQueryRead, NativePublicQueryShape.Ordering)]
    public async Task PublicJsonQueryNullElementsReachTheirOwningDiagnostics(GrainReadKind kind, NativePublicQueryShape shape)
    {
        using var database = new TestDatabase();
        database.Configure(NativePublicReadFixture.Collection, ResourceKind.Collection);
        var query = NativePublicReadFixture.Query(database, shape);
        var json = JsonDefaults.Serialize(query);
        var decoded = JsonDefaults.Deserialize<AstQueryRequest>(json);
        var original = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(database.Database)
            .ExecuteAst(NativeAuthorityFixture.Root, decoded));
        var payload = NativePublicReadFixture.Encode(query, kind, database);
        var request = NativePublicReadFixture.Signed(database, payload, kind);
        var position = database.Store.Position;
        var native = Assert.ThrowsExactly<KeyLoadException>(() => NativePublicReadFixture.ExecuteQuery(database, request));
        await Assert.That(native.Code).IsEqualTo(NativePublicReadFixture.Code(shape));
        await Assert.That(native.Message).IsEqualTo(NativePublicReadFixture.Detail(shape));
        await Assert.That(native.Code).IsEqualTo(original.Code);
        await Assert.That(native.Message).IsEqualTo(original.Message);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task SignedPublicQueryNullElementDoesNotPreemptCurrentPrincipalReload()
    {
        using var database = new TestDatabase();
        var query = NativePublicReadFixture.Query(database, NativePublicQueryShape.Projection);
        var payload = NativePublicReadFixture.Encode(query, GrainReadKind.AstQuery, database);
        var request = NativePublicReadFixture.Signed(database, payload, GrainReadKind.AstQuery,
            NativeAuthorityFixture.OtherPrincipal);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativePublicReadFixture.ExecuteQuery(database, request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicJsonNullTraversalLabelsRetainSuccessfulFiltering(bool onlyNull)
    {
        using var database = new TestDatabase();
        database.Configure(NativePublicReadFixture.Collection, ResourceKind.Collection);
        database.Configure(NativePublicReadFixture.Graph, ResourceKind.Graph);
        var start = new EntityRef(database.Partition, NativePublicReadFixture.Collection, NativePublicReadFixture.Start);
        var end = start with { Id = NativePublicReadFixture.End };
        database.Commit(new PutDocument(start.Collection, start.Id, "{}"), new PutDocument(end.Collection, end.Id, "{}"),
            new UpsertEdge(NativePublicReadFixture.Graph, NativePublicReadFixture.Edge, start, end, NativePublicReadFixture.Label));
        ImmutableArray<string> labels = onlyNull ? [null!] : [null!, NativePublicReadFixture.Label];
        var query = new TraverseRequest(database.Partition, NativePublicReadFixture.Graph, start, Labels: labels);
        var publicValue = JsonDefaults.Deserialize<TraverseRequest>(JsonDefaults.Serialize(query));
        var original = database.Database.Traverse(NativeAuthorityFixture.Root, publicValue.Partition, publicValue.Graph,
            publicValue.Start, labels: publicValue.Labels?.ToArray());
        var request = NativePublicReadFixture.Signed(database,
            NativePublicReadFixture.Encode(query, GrainReadKind.Traverse, database), GrainReadKind.Traverse);
        var principal = GrainRequestAuthority.Reload(database.Database, request.Envelope.PrincipalId!, TimeProvider.System);
        var native = (global::KeyLoad.GraphTraversal)new GrainCoreReadCapabilities(database.Database).Execute(GrainReadKind.Traverse,
            principal.Id, request.Payload, CancellationToken.None)!;
        await Assert.That(native.Vertices.SequenceEqual(original.Vertices)).IsTrue();
        await Assert.That(native.Edges.SequenceEqual(original.Edges)).IsTrue();
        await Assert.That(native.Vertices.Length).IsEqualTo(onlyNull ? 1 : 2);
        await Assert.That(native.Edges.Length).IsEqualTo(onlyNull ? 0 : 1);
    }
}
