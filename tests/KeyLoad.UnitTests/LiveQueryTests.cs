using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests;

public sealed class LiveQueryTests
{
    private sealed record Order(decimal Number, string Status);
    private static AstQueryRequest Query(TestDatabase db) => KeyLoadQuery<Order>.From(db.Partition, "orders")
        .Where(order => order.Status == "open").Take(100).ToRequest(true);
    private static void Apply(Dictionary<string, QueryRow> rows, LiveQueryPage page)
    {
        foreach (var change in page.Changes)
            if (change.Kind == LiveQueryChangeKind.Remove) rows.Remove(change.Reference.Id);
            else rows[change.Reference.Id] = change.Row!;
    }
    private static void Same(QueryRow[] expected, IEnumerable<QueryRow> actual)
        => Assert.Equal(JsonDefaults.Serialize(expected.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray()),
            JsonDefaults.Serialize(actual.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray()));
    [Fact]
    public void ScalarLiveDeltasMatchTheSharedQueryOracleAcrossSeededMutationsAndReplay()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database); var query = Query(db); var snapshot = engine.StartLiveQuery("root", new(query));
        var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal); var cursor = snapshot.Cursor;
        var revisions = new long[12]; var deleted = Enumerable.Repeat(true, 12).ToArray(); var random = new Random(41179);
        for (var trial = 0; trial < 100; trial++)
        {
            var index = random.Next(12); var id = "order-" + index;
            if (!deleted[index] && random.Next(4) == 0)
            { db.Commit(new DeleteDocument("orders", id, revisions[index])); deleted[index] = true; }
            else
            {
                var json = JsonSerializer.Serialize(new Order(trial, random.Next(2) == 0 ? "open" : "closed"), JsonDefaults.Options);
                db.Commit(new PutDocument("orders", id, json, revisions[index], ExplicitReplacement: true)); deleted[index] = false;
            }
            revisions[index]++;
            var page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3));
            var replay = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3));
            Assert.Equal(JsonDefaults.Serialize(page.Changes), JsonDefaults.Serialize(replay.Changes));
            Apply(rows, page); Apply(rows, replay); cursor = page.Cursor;
            while (page.HasMore) { page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3)); Apply(rows, page); cursor = page.Cursor; }
            Same(engine.ExecuteAst("root", query).Rows, rows.Values);
        }
    }
    [Fact]
    public async Task ConcurrentInitialSnapshotAndTailNeverLoseACommittedDocument()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); var engine = new QueryEngine(db.Database); var query = Query(db);
        var writer = Task.Run(() =>
        {
            for (var index = 0; index < 60; index++) db.Commit(new PutDocument("orders", "order-" + index,
                JsonSerializer.Serialize(new Order(index, "open"), JsonDefaults.Options), 0));
        }, TestContext.Current.CancellationToken);
        var snapshot = engine.StartLiveQuery("root", new(query)); var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        await writer; var cursor = snapshot.Cursor; LiveQueryPage page;
        do { page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 4)); Apply(rows, page); cursor = page.Cursor; } while (page.HasMore);
        Assert.Equal(60, rows.Count); Same(engine.ExecuteAst("root", query).Rows, rows.Values);
    }
    [Fact]
    public void ProtectedPredicateCanUseItsGrantWhileAllReturnedRowsRemainProjected()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection, fields: [new("/status", "pii")]);
        var reader = new PrincipalRecord("reader", "tenant", [new("database", "orders", Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], ["pii.use"]);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        var query = Query(db); var engine = new QueryEngine(db.Database); var snapshot = engine.StartLiveQuery("reader", new(query));
        db.Commit(new PutDocument("orders", "a", "{\"number\":1,\"status\":\"open\"}"));
        var delta = engine.ReadLiveQuery("reader", new(query, snapshot.Cursor)); var row = Assert.Single(delta.Changes).Row!;
        Assert.True(row.Redacted); Assert.DoesNotContain("open", row.Json); Assert.DoesNotContain("status", row.Json);
        var other = reader with { Id = "ordinary", FieldGrants = [] };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(other)).Get<PrincipalRecord>();
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => engine.StartLiveQuery("ordinary", new(query))).Code);
        Assert.Equal(ErrorCode.PermissionDenied, Assert.Throws<KeyLoadException>(() => engine.ReadLiveQuery("ordinary", new(query, snapshot.Cursor))).Code);
    }
    [Fact]
    public void PolicyChangeAndRowAclChangeRequireANewSnapshot()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        var reader = new PrincipalRecord("reader", "tenant", [new("database", "orders", Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], [])
            { RestrictRows = true, OwnerId = "alice" };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}", Access: new("alice")));
        var query = Query(db); var engine = new QueryEngine(db.Database); var snapshot = engine.StartLiveQuery("reader", new(query));
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}", 1, new("bob"), true));
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => engine.ReadLiveQuery("reader", new(query, snapshot.Cursor))).Code);
        var fresh = engine.StartLiveQuery("reader", new(query)); Assert.Empty(fresh.Rows);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader with { PolicyEpoch = 2 })).Get<PrincipalRecord>();
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => engine.ReadLiveQuery("reader", new(query, fresh.Cursor))).Code);
    }
    [Fact]
    public void UnsupportedRankingIncompleteSnapshotsAndDifferentQueryCursorsAreRejected()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}"), new PutDocument("orders", "b", "{\"status\":\"open\"}"));
        var query = Query(db); var engine = new QueryEngine(db.Database);
        Assert.Equal(ErrorCode.BudgetExceeded, Assert.Throws<KeyLoadException>(() => engine.StartLiveQuery("root", new(query with { Query = query.Query with { Limit = 1 } }))).Code);
        Assert.Equal(ErrorCode.UnsupportedCapability, Assert.Throws<KeyLoadException>(() => engine.StartLiveQuery("root", new(query with { Query = query.Query with { Order = [new("/number", false)] } }))).Code);
        var snapshot = engine.StartLiveQuery("root", new(query)); var other = query with { Query = query.Query with { Filter = null } };
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => engine.ReadLiveQuery("root", new(other, snapshot.Cursor))).Code);
    }
    [Fact]
    public void NonMatchingChangesAdvanceTheCursorAndHistoryLossRequiresResynchronization()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); var engine = new QueryEngine(db.Database); var query = Query(db);
        var snapshot = engine.StartLiveQuery("root", new(query)); db.Commit(new PutDocument("orders", "a", "{\"status\":\"closed\"}"));
        var page = engine.ReadLiveQuery("root", new(query, snapshot.Cursor)); Assert.Empty(page.Changes); Assert.Equal(1, page.ThroughSequence);
        var id = Guid.NewGuid(); db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 1), id: id).Get<OutboxHead>();
        Assert.Equal(ErrorCode.HistoryUnavailable, Assert.Throws<KeyLoadException>(() => engine.ReadLiveQuery("root", new(query, snapshot.Cursor))).Code);
        Assert.Empty(engine.ReadLiveQuery("root", new(query, page.Cursor)).Changes);
    }
}
