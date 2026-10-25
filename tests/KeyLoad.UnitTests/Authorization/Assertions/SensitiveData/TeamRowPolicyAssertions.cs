using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Authorization;

internal static class TeamRowPolicyAssertions
{
    private const string Sql = "SELECT * FROM \"team-rows\" ORDER BY id";
    private const string Scan = "bounded-full-scan";
    private const string ForeignTenant = "foreign-tenant";
    private const string ForgedId = "forged-team";
    private const string InvalidTeam = " ";
    private const string PutKind = "putDocument";
    private const string ProjectPrefix = "project-";
    private const int OriginalMembershipSlots = 256;
    private const long ForgedPolicyEpoch = 2;
    private const long AbsentRevision = 0;
    private const int FirstMembershipIndex = 0;

    internal static string[] Image(ZoneTreeStore store) => CanonicalStoreImage.Bytes(store);

    internal static async Task VisibleAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        long revision, string json, CancellationToken token)
    {
        QueryRow[] rows = [new(TeamRowPolicyFlow.Row, revision, json, false, [])];
        await ReadAsync(db, database, queries, store, rows, token);
    }

    internal static async Task HiddenAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store, CancellationToken token)
        => await ReadAsync(db, database, queries, store, [], token);

    private static async Task ReadAsync(TestDatabase db, DatabaseEngine database, QueryEngine queries, ZoneTreeStore store,
        QueryRow[] expected, CancellationToken token)
    {
        var image = Image(store);
        var cut = store.Position;
        var request = new QueryRequest(db.Partition, Sql, AllowFullScan: true);
        var page = queries.Execute(TeamRowPolicyFlow.Reader, request, cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        var plan = JsonSerializer.Serialize(new
        {
            AccessPath = Scan,
            AtomicPartition = db.Partition.AtomicPartitionId
        }, JsonDefaults.Options);
        QueryRow[] explanation = [new("explain", AbsentRevision, plan)];
        var explanationRequest = new AstQueryRequest(db.Partition,
            new(TeamRowPolicyFlow.Collection, null, [new(global::KeyLoad.Query.QueryExecution.SqlSyntax.Star,
                global::KeyLoad.Query.QueryExecution.SqlSyntax.Star)], null,
                [new(global::KeyLoad.Query.QueryExecution.SqlSyntax.MetadataIdPath, false)], global::KeyLoad.Query.QueryExecution.SqlSyntax.DefaultLimit, Explain: true), AllowFullScan: true);
        var explain = queries.ExecuteAst(TeamRowPolicyFlow.Reader, explanationRequest, cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(explain.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(explanation))).IsTrue();
        await Assert.That(explain.CutPosition).IsEqualTo(cut);
        await Assert.That(explain.Cursor).IsNull();
        await Assert.That(explain.AccessPath).IsEqualTo(Scan);
        var reference = new EntityRef(db.Partition, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row);
        var document = database.GetDocument(TeamRowPolicyFlow.Reader, reference, cancellationToken: token);
        DocumentResult? pointExpected = expected.Length == FirstMembershipIndex ? null
            : new(db.ExpectedReference(reference), expected[FirstMembershipIndex].Revision, expected[FirstMembershipIndex].Json, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(pointExpected))).IsTrue();
        await UnchangedAsync(store, image, cut);
    }

    internal static async Task RevokedAsync(TestDatabase db, QueryEngine queries, ZoneTreeStore store, CancellationToken token)
    {
        var image = Image(store);
        var cut = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(TeamRowPolicyFlow.Reader,
            new(db.Partition, Sql, AllowFullScan: true), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await UnchangedAsync(store, image, cut);
    }

    internal static async Task InvalidMembershipAsync(TestDatabase db, PrincipalRecord original)
    {
        var key = KeySpace.Principal(original.Id);
        var bytes = db.Store.Read(view => view.ReadOwnedValue(key))!;
        var projects = Enumerable.Range(FirstMembershipIndex, OriginalMembershipSlots).Select(index => ProjectPrefix + index.ToString(CultureInfo.InvariantCulture)).ToImmutableArray();
        foreach (var candidate in new[] { original with { Teams = [InvalidTeam], PolicyEpoch = ForgedPolicyEpoch },
            original with { Projects = projects, PolicyEpoch = ForgedPolicyEpoch } })
        {
            var refusal = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(candidate));
            await Assert.That(Errors.Code(refusal.Problem)).IsEqualTo(ErrorCode.Validation);
            await Assert.That(db.Store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(bytes)).IsTrue();
        }
    }

    internal static async Task ForgedAsync(TestDatabase db, PrincipalRecord principal, CancellationToken token)
    {
        var key = KeySpace.Principal(principal.Id);
        var bytes = db.Store.Read(view => view.ReadOwnedValue(key))!;
        var forged = db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal with
            {
                Teams = [TeamRowPolicyFlow.ForeignTeam],
                PolicyEpoch = ForgedPolicyEpoch
            }), principal.Id);
        await Assert.That(Errors.Code(forged.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(bytes)).IsTrue();
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(TeamRowPolicyFlow.Collection, ForgedId, TeamRowPolicyFlow.OriginalJson,
                AbsentRevision, new(TeamId: TeamRowPolicyFlow.ForeignTeam))]);
        var refused = db.Submit(OperationKind.Batch, command, principal.Id, command.CommandId);
        await Assert.That(Errors.Code(refused.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.GetDocument(TeamRowPolicyFlow.Root, new(db.Partition, TeamRowPolicyFlow.Collection, ForgedId), cancellationToken: token)).IsNull();
        var retained = TeamRowPolicyFlow.Outcome(db.Store, command);
        await OldRefusedAsync(db.Database, db.Store, command, retained, ErrorCode.PermissionDenied, token);
        var image = Image(db.Store);
        var cut = db.Store.Position;
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.GetDocument(principal.Id,
            new(db.Partition with
            {
                TenantId = ForeignTenant
            }, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row), cancellationToken: token));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await UnchangedAsync(db.Store, image, cut);
    }

    internal static async Task ReceiptAsync(ZoneTreeStore store, CommandRequest command, CommitReceipt receipt, long revision, long epoch)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        var outcome = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(command.Partition, TeamRowPolicyFlow.Reader, command.CommandId)))!;
        await Assert.That(receipt.Token.Position).IsEqualTo(outcome.CanonicalSequence);

        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        MutationReceipt[] mutations = [new(PutKind, TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row, revision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(mutations))).IsTrue();
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(epoch);
        await Assert.That(outcome.Incarnation).IsEqualTo(store.Identity.Incarnation);
    }

    internal static async Task OldRefusedAsync(DatabaseEngine engine, ZoneTreeStore store, CommandRequest original,
        byte[] outcome, ErrorCode error, CancellationToken token)
    {
        var image = Image(store);
        var cut = store.Position;
        var refusal = engine.ApplyEmbedded(TeamRowPolicyFlow.Operation(store, original), cancellationToken: token);
        await Assert.That(Errors.Code(refusal.Problem)).IsEqualTo(error);
        await Assert.That(TeamRowPolicyFlow.Outcome(store, original).AsSpan().SequenceEqual(outcome)).IsTrue();
        await UnchangedAsync(store, image, cut);
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long cut)
    {
        await Assert.That(Image(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }
}
