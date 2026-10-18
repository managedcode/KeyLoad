using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityWriteAssertions
{
    private const string Scan = "bounded-full-scan";
    private const string ExplainId = "explain";
    private const long NoRevision = 0;
    private const long OriginalRootEpoch = 1;
    private const string TombstoneJson = "{}";

    internal static async Task RowsAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        long rowRevision, string rowJson, bool visible, CancellationToken token)
        => await ReadAsync(db, database, queries, store, db.Partition, rowRevision, rowJson, visible, token);

    internal static async Task ColdRowsAsync(TestDatabase creation, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        PartitionRef partition, CancellationToken token)
        => await ReadAsync(creation, database, queries, store, partition, TeamRowPolicyFlow.HealthyRevision,
            TeamRowPolicyFlow.HealthyJson, false, token);

    private static async Task ReadAsync(TestDatabase creation, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        PartitionRef partition, long rowRevision, string rowJson, bool visible, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        QueryRow[] expected = visible
            ? [new(TeamRowPolicyFlow.ForeignTeam, GenerationVisibilityWriteFlow.VisibleRevision, TeamRowPolicyFlow.OriginalJson, false, []),
                new(TeamRowPolicyFlow.Row, rowRevision, rowJson, false, [])]
            : [new(TeamRowPolicyFlow.Row, rowRevision, rowJson, false, [])];
        var request = new QueryRequest(partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true);
        await PageAsync(queries.Execute(TeamRowPolicyFlow.Reader, request, cancellationToken: token), expected, cut);
        var plan = JsonSerializer.Serialize(new
        {
            AccessPath = Scan,
            AtomicPartition = partition.AtomicPartitionId,
            ScanBudget = UnitExecutionOptions.DatabaseLimits().Value.MaxScanRecords
        }, JsonDefaults.Options);
        var explanation = new global::KeyLoad.Query.AstQueryRequest(partition,
            new(TeamRowPolicyFlow.Collection, null, [new(global::KeyLoad.Query.Features.QueryExecution.SqlSyntax.Star,
                global::KeyLoad.Query.Features.QueryExecution.SqlSyntax.Star)], null,
                [new(global::KeyLoad.Query.Features.QueryExecution.SqlSyntax.MetadataIdPath, false)],
                global::KeyLoad.Query.Features.QueryExecution.SqlSyntax.DefaultLimit, Explain: true), AllowFullScan: true);
        await PageAsync(queries.ExecuteAst(TeamRowPolicyFlow.Reader, explanation,
            cancellationToken: token), [new(ExplainId, NoRevision, plan)], cut);
        var reference = new EntityRef(partition, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam);
        DocumentResult? expectedPoint = visible ? new(creation.ExpectedReference(reference), GenerationVisibilityWriteFlow.VisibleRevision,
            TeamRowPolicyFlow.OriginalJson, false, []) : null;
        await Assert.That(JsonDefaults.Serialize(database.GetDocument(TeamRowPolicyFlow.Reader, reference,
            cancellationToken: token)).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedPoint))).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    private static async Task PageAsync(QueryPage page, QueryRow[] expected, long cut)
    {
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
    }

    internal static async Task ReceiptAsync(TestDatabase db, CommandRequest command, CommitReceipt receipt, CommitToken expectedToken, long previousPosition, string kind, long revision)
    {
        CommitReceipt expected = new(command.CommandId, expectedToken,
            [new(kind, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam, revision)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(previousPosition + 1L);
        var outcome = db.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(db.Partition,
            TeamRowPolicyFlow.Root, command.CommandId)))!;
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(OriginalRootEpoch);
        await Assert.That(outcome.Incarnation).IsEqualTo(db.Store.Identity.Incarnation);
    }

    internal static async Task RecordAsync(TestDatabase creation, ZoneTreeStore store, PartitionRef partition, long revision, bool deleted,
        string team, DateTimeOffset before, DateTimeOffset after)
    {
        var reference = creation.ExpectedReference(new(partition, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam));
        var record = store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)))!;
        await Assert.That(JsonDefaults.Serialize(record.Reference).AsSpan().SequenceEqual(JsonDefaults.Serialize(reference))).IsTrue();
        await Assert.That(record.Revision).IsEqualTo(revision);
        await Assert.That(record.Json).IsEqualTo(deleted ? TombstoneJson : TeamRowPolicyFlow.OriginalJson);
        await Assert.That(JsonDefaults.Serialize(record.Access).AsSpan().SequenceEqual(JsonDefaults.Serialize(new RowAccess(TeamId: team)))).IsTrue();
        await Assert.That(record.Deleted).IsEqualTo(deleted);
        await Assert.That(record.UpdatedAt).IsGreaterThanOrEqualTo(before);
        await Assert.That(record.UpdatedAt).IsLessThanOrEqualTo(after);
    }
}
