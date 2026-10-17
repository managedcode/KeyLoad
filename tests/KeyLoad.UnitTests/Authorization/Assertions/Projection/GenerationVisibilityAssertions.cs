using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityAssertions
{
    internal const string Sql = "SELECT * FROM \"team-rows\" ORDER BY id";
    internal const string GraphSql = "SELECT * FROM \"team-vertices\" ORDER BY id";
    private const string Scan = "bounded-full-scan";
    private const long FirstRevision = 1;
    private const string EmptyJson = "{}";
    private const string Start = "a";
    private const string Middle = "b";
    private const string End = "c";
    private const string MissingPin = "The completed canonical scan did not publish its admitted visibility set.";

    internal static RowVisibilityLease Hold(TestDatabase db, RowVisibilityCache cache)
        => db.Database.WithQueryView(TeamRowPolicyFlow.Reader, db.Partition, TeamRowPolicyFlow.Collection,
            (view, principal, resource) => cache.Acquire(db.Database.CaptureRowVisibility(principal, resource,
                db.Partition, resource.Name, db.Database.DocumentEpoch(view, db.Partition, resource.Name)))
                ?? throw new InvalidOperationException(MissingPin));

    internal static async Task HealthyAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries,
        ZoneTreeStore store, long revision, string json, CancellationToken token)
    {
        await TeamRowPolicyAssertions.VisibleAsync(db, database, queries, store, revision, json, token);
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        QueryRow[] expected = [new(Start, FirstRevision, EmptyJson, false, []),
            new(Middle, FirstRevision, EmptyJson, false, []), new(End, FirstRevision, EmptyJson, false, [])];
        var page = queries.Execute(TeamRowPolicyFlow.Reader,
            new(db.Partition, GraphSql, AllowFullScan: true), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await TeamGraphVisibilityAssertions.ReadAsync(database, store, db.Partition, true, token);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}
