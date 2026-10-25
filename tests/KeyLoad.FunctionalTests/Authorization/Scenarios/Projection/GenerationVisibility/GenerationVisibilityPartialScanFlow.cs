using KeyLoad.Core.Authorization;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class GenerationVisibilityPartialScanFlow
{
    internal const string Reader = "visibility-partial-scan-reader";
    private const string MissingCache = "The shared budget refused the bounded scan owner.";
    private const string MissingDraft = "The current authorized scope refused its genuine draft.";
    private const string Scan = "bounded-full-scan";
    private const string AbsentRow = "visibility-not-seeded";

    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        await GenerationVisibilityFlow.ConfigureAsync(db, new(Reader, GenerationVisibilityFlow.Tenant,
            [new(GenerationVisibilityFlow.DatabaseScope, TeamRowPolicyFlow.Collection,
                Capability.DocumentsRead | Capability.Query)], [])
        {
            PolicyEpoch = GenerationVisibilityFlow.OriginalEpoch
        });
        await HealthyAsync(db.Database, db.Store, db.Partition, GenerationVisibilityFlow.InitialRevision,
            TeamRowPolicyFlow.OriginalJson, GenerationVisibilityFlow.InitialRevision, token);
    }

    private static RowVisibilityScope Scope(DatabaseEngine database, PartitionRef partition)
        => database.WithQueryView(Reader, partition, TeamRowPolicyFlow.Collection,
            (view, principal, resource) => database.CaptureRowVisibility(principal, resource, partition,
                resource.Name, database.DocumentEpoch(view, partition, resource.Name)));

    internal static async Task HealthyAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        long revision, string json, long foreignRevision, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        QueryRow[] expected = [new(TeamRowPolicyFlow.ForeignTeam, foreignRevision,
            TeamRowPolicyFlow.OriginalJson, false, []), new(TeamRowPolicyFlow.Row, revision, json, false, [])];
        var page = queries.Execute(Reader, new(partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true),
            cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        var cache = database.RowVisibility ?? throw new InvalidOperationException(MissingCache);
        using var complete = cache.Acquire(Scope(database, partition))
            ?? throw new InvalidOperationException(MissingDraft);
        await Assert.That(complete.Contains(TeamRowPolicyFlow.ForeignTeam)).IsTrue();
        await Assert.That(complete.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await Assert.That(complete.Contains(AbsentRow)).IsFalse();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}
