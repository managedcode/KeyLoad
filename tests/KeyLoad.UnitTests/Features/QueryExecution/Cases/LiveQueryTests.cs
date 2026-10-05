using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class LiveQueryTests
{
    private sealed record LiveQueryOrder(decimal Number, string Status);
    private sealed record LiveQueryExpectation(bool Changed, LiveQueryChangeKind Kind);

    private static AstQueryRequest Query(TestDatabase db) => KeyLoadQuery.From<LiveQueryOrder>(db.Partition, "orders", UnitClientOptions.Translation())
        .Where(order => order.Status == "open").Take(100).ToRequest(true);
    private static void Apply(Dictionary<string, QueryRow> rows, LiveQueryPage page)
    {
        foreach (var change in page.Changes)
        {
            if (change.Kind == LiveQueryChangeKind.Remove)
            {
                rows.Remove(change.Reference.Id);
            }
            else
            {
                rows[change.Reference.Id] = change.Row!;
            }
        }
    }
    private static async Task Same(IEnumerable<QueryRow> expected, IEnumerable<QueryRow> actual)
        => await Assert.That(JsonDefaults.Serialize(actual.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray())
            .SequenceEqual(JsonDefaults.Serialize(expected.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray()))).IsTrue();

    private static LiveQueryExpectation CommitScheduledMutation(TestDatabase db, int index, int trial, int round,
        long[] revisions, bool[] deleted)
    {
        var id = "order-" + index.ToString(CultureInfo.InvariantCulture);
        if (round == LiveQueryTestSchedule.DeleteRound)
        {
            db.Commit(new DeleteDocument("orders", id, revisions[index]));
            deleted[index] = true;
        }
        else
        {
            var status = round is LiveQueryTestSchedule.LeaveRound or LiveQueryTestSchedule.NonmatchingUpdateRound or LiveQueryTestSchedule.NonmatchingInsertRound
                ? "closed"
                : "open";
            var json = JsonSerializer.Serialize(new LiveQueryOrder(trial, status), JsonDefaults.Options);
            db.Commit(new PutDocument("orders", id, json, revisions[index], ExplicitReplacement: true));
            deleted[index] = false;
        }

        revisions[index]++;
        return new(round is not LiveQueryTestSchedule.NonmatchingUpdateRound and not LiveQueryTestSchedule.NonmatchingInsertRound,
            round is LiveQueryTestSchedule.LeaveRound or LiveQueryTestSchedule.DeleteRound
                ? LiveQueryChangeKind.Remove
                : LiveQueryChangeKind.Upsert);
    }

    [Test]
    public async Task ScalarLiveDeltasMatchTheSharedQueryOracleAcrossSeededMutationsAndReplay()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = Query(db);
        var snapshot = engine.StartLiveQuery("root", new(query));
        var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        var cursor = snapshot.Cursor;
        var revisions = new long[LiveQueryTestSchedule.IdentityCount];
        var deleted = Enumerable.Repeat(true, LiveQueryTestSchedule.IdentityCount).ToArray();
        var identityVisits = new int[LiveQueryTestSchedule.FullRounds * LiveQueryTestSchedule.IdentityCount];
        var transitions = new int[LiveQueryTestSchedule.TransitionRounds.Length];
        var previousSequence = snapshot.ThroughSequence;
        for (var trial = 0; trial < LiveQueryTestSchedule.MutationCount; trial++)
        {
            var index = LiveQueryTestSchedule.IndexForTrial(trial);
            var id = "order-" + index.ToString(CultureInfo.InvariantCulture);
            var round = trial / LiveQueryTestSchedule.IdentityCount;
            if (round < LiveQueryTestSchedule.FullRounds)
            {
                identityVisits[round * LiveQueryTestSchedule.IdentityCount + index]++;
            }
            await Assert.That(deleted[index]).IsEqualTo(round is LiveQueryTestSchedule.InsertRound or LiveQueryTestSchedule.NonmatchingInsertRound);
            var expected = CommitScheduledMutation(db, index, trial, round, revisions, deleted);
            var page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3));
            var replay = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3));
            await Assert.That(JsonDefaults.Serialize(replay.Changes).SequenceEqual(JsonDefaults.Serialize(page.Changes))).IsTrue();
            await Assert.That(page.ThroughSequence).IsEqualTo(previousSequence + 1);
            await Assert.That(page.Changes.Length).IsEqualTo(expected.Changed ? 1 : 0);
            if (expected.Changed)
            {
                await Assert.That(page.Changes[0].Kind).IsEqualTo(expected.Kind);
                await Assert.That(page.Changes[0].Reference.Id).IsEqualTo(id);
            }
            transitions[round]++;

            Apply(rows, page);
            Apply(rows, replay);
            cursor = page.Cursor;
            while (page.HasMore)
            { page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 3)); Apply(rows, page); cursor = page.Cursor; }
            previousSequence = page.ThroughSequence;
            await Same(engine.ExecuteAst("root", query).Rows, rows.Values);
        }

        await QueryWorkloadCoverageAssertions.AssertLiveMutationCoverage(identityVisits, transitions);
    }
    [Test]
    public async Task ConcurrentInitialSnapshotAndTailNeverLoseACommittedDocument()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = Query(db);
        var writer = Task.Run(() =>
        {
            for (var index = 0; index < 60; index++)
            {
                db.Commit(new PutDocument("orders", "order-" + index.ToString(CultureInfo.InvariantCulture),
                    JsonSerializer.Serialize(new LiveQueryOrder(index, "open"), JsonDefaults.Options), 0));
            }
        }, TestContext.Current!.Execution.CancellationToken);
        var snapshot = engine.StartLiveQuery("root", new(query));
        var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        await writer;
        var cursor = snapshot.Cursor;
        LiveQueryPage page;
        do
        { page = engine.ReadLiveQuery("root", new(query, cursor, Limit: 4)); Apply(rows, page); cursor = page.Cursor; } while (page.HasMore);
        await Assert.That(rows.Count).IsEqualTo(60);
        await Same(engine.ExecuteAst("root", query).Rows, rows.Values);
    }
    [Test]
    public async Task ProtectedPredicateCanUseItsGrantWhileAllReturnedRowsRemainProjected()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection, fields: [new("/status", "pii")]);
        var reader = new PrincipalRecord("reader", "tenant", [new("database", "orders", Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], ["pii.use"]);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        var query = Query(db);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var snapshot = engine.StartLiveQuery("reader", new(query));
        db.Commit(new PutDocument("orders", "a", "{\"number\":1,\"status\":\"open\"}"));
        var delta = engine.ReadLiveQuery("reader", new(query, snapshot.Cursor));
        var row = (await Assert.That(delta.Changes).HasSingleItem()).Row!;
        await Assert.That(row.Redacted).IsTrue();
        await Assert.That(row.Json).DoesNotContain("open");
        await Assert.That(row.Json).DoesNotContain("status");
        var other = reader with { Id = "ordinary", FieldGrants = [] };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(other)).Get<PrincipalRecord>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.StartLiveQuery("ordinary", new(query))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery("ordinary", new(query, snapshot.Cursor))).Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
    [Test]
    public async Task PolicyChangeAndRowAclChangeRequireANewSnapshot()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var reader = new PrincipalRecord("reader", "tenant", [new("database", "orders", Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], [])
        { RestrictRows = true, OwnerId = "alice" };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}", Access: new("alice")));
        var query = Query(db);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var snapshot = engine.StartLiveQuery("reader", new(query));
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}", 1, new("bob"), true));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery("reader", new(query, snapshot.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var fresh = engine.StartLiveQuery("reader", new(query));
        await Assert.That(fresh.Rows).IsEmpty();
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader with { PolicyEpoch = 2 })).Get<PrincipalRecord>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery("reader", new(query, fresh.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }
    [Test]
    public async Task UnsupportedRankingIncompleteSnapshotsAndDifferentQueryCursorsAreRejected()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"open\"}"), new PutDocument("orders", "b", "{\"status\":\"open\"}"));
        var query = Query(db);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.StartLiveQuery("root", new(query with { Query = query.Query with { Limit = 1 } }))).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.StartLiveQuery("root", new(query with { Query = query.Query with { Order = [new("/number", false)] } }))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var snapshot = engine.StartLiveQuery("root", new(query));
        var other = query with { Query = query.Query with { Filter = null } };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery("root", new(other, snapshot.Cursor))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }
    [Test]
    public async Task NonMatchingChangesAdvanceTheCursorAndHistoryLossRequiresResynchronization()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = Query(db);
        var snapshot = engine.StartLiveQuery("root", new(query));
        db.Commit(new PutDocument("orders", "a", "{\"status\":\"closed\"}"));
        var page = engine.ReadLiveQuery("root", new(query, snapshot.Cursor));
        await Assert.That(page.Changes).IsEmpty();
        await Assert.That(page.ThroughSequence).IsEqualTo(1);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, 1), id: id).Get<OutboxHead>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadLiveQuery("root", new(query, snapshot.Cursor))).Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(engine.ReadLiveQuery("root", new(query, page.Cursor)).Changes).IsEmpty();
    }
}
