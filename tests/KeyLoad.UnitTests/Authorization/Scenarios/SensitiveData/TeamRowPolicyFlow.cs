using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class TeamRowPolicyFlow
{
    internal const string Reader = "team-reader";
    internal const string Collection = "team-rows";
    internal const string Team = "team-alpha";
    internal const string ForeignTeam = "team-beta";
    internal const string Row = "team-row";
    internal const string Root = "root";
    internal const string OriginalJson = "{\"value\":1}";
    internal const string CurrentJson = "{\"value\":2}";
    internal const string HealthyJson = "{\"value\":3}";
    private const string DatabaseScope = "database";
    private const string Tenant = "tenant";
    private const long OriginalEpoch = 1;
    private const long RemovedEpoch = 2;
    private const long RevokedEpoch = 3;
    internal const long RepairedEpoch = 4;
    private const long OriginalRevision = 1;
    private const long CurrentRevision = 2;
    internal const long HealthyRevision = 3;
    private const string MissingOutcome = "The original acknowledged team-row outcome is absent.";

    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, Row, OriginalJson, Access: new(TeamId: Team)),
            new PutDocument(Collection, ForeignTeam, OriginalJson, Access: new(TeamId: ForeignTeam)));
        TeamGraphVisibilityAssertions.Seed(db);
        var principal = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
                new(DatabaseScope, TeamGraphVisibilityAssertions.Collection, Capability.DocumentsRead),
                new(DatabaseScope, TeamGraphVisibilityAssertions.Graph, Capability.GraphRead)], [])
        {
            OwnerId = Reader,
            RestrictRows = true,
            Teams = [Team],
            PolicyEpoch = OriginalEpoch
        };
        await ConfigureAsync(db, principal);
        await TeamRowPolicyAssertions.InvalidMembershipAsync(db, principal);
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await TeamRowPolicyAssertions.VisibleAsync(db, db.Database, queries, db.Store, OriginalRevision, OriginalJson, token);
        await TeamGraphVisibilityAssertions.ReadAsync(db, db.Database, db.Store, db.Partition, true, token);
        await TeamRowPolicyAssertions.ForgedAsync(db, principal, token);
        var original = Write(db, OriginalRevision, CurrentJson);
        var receipt = db.Submit(OperationKind.Batch, original, Reader, original.CommandId).Get<CommitReceipt>();
        await TeamRowPolicyAssertions.ReceiptAsync(db.Store, original, receipt, CurrentRevision, OriginalEpoch);
        var outcome = Outcome(db.Store, original);
        await ReplayAsync(db.Database, db.Store, original, receipt, outcome, token);
        await ConfigureAsync(db, principal with
        {
            Teams = [],
            PolicyEpoch = RemovedEpoch
        });
        await TeamRowPolicyAssertions.HiddenAsync(db, db.Database, queries, db.Store, token);
        await TeamGraphVisibilityAssertions.ReadAsync(db, db.Database, db.Store, db.Partition, false, token);
        await TeamRowPolicyAssertions.OldRefusedAsync(db.Database, db.Store, original, outcome, ErrorCode.PermissionDenied, token);
        await ConfigureAsync(db, principal with
        {
            Revoked = true,
            PolicyEpoch = RevokedEpoch
        });
        await TeamRowPolicyAssertions.RevokedAsync(db, queries, db.Store, token);
        await TeamGraphVisibilityAssertions.RevokedAsync(db.Database, db.Store, db.Partition, token);
        await ConfigureAsync(db, principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        await TeamRowPolicyAssertions.VisibleAsync(db, db.Database, queries, db.Store, CurrentRevision, CurrentJson, token);
        await TeamGraphVisibilityAssertions.ReadAsync(db, db.Database, db.Store, db.Partition, true, token);
        await TeamRowPolicyAssertions.OldRefusedAsync(db.Database, db.Store, original, outcome, ErrorCode.PermissionDenied, token);
        var current = Write(db, CurrentRevision, HealthyJson);
        var fresh = db.Submit(OperationKind.Batch, current, Reader, current.CommandId).Get<CommitReceipt>();
        await TeamRowPolicyAssertions.ReceiptAsync(db.Store, current, fresh, HealthyRevision, RepairedEpoch);
        var currentOutcome = Outcome(db.Store, current);
        await ReplayAsync(db.Database, db.Store, current, fresh, currentOutcome, token);
        await TeamRowPolicyCold.RunAsync(db, original, outcome, current, fresh, currentOutcome, token);
    }

    private static CommandRequest Write(TestDatabase db, long revision, string json)
        => new(Guid.NewGuid(), db.Partition, [new PutDocument(Collection, Row, json, revision, new(TeamId: Team))]);

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord expected)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static Command<DatabaseCommand> Operation(ZoneTreeStore store, CommandRequest command,
        string principalId = Reader)
    {
        var stored = store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.PartitionOutcome(command.Partition, principalId, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);
        return stored.PreparedCommand;
    }

    internal static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, Reader, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);

    internal static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt, byte[] outcome, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var replay = engine.ApplyEmbedded(Operation(store, command), cancellationToken: token).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(Outcome(store, command).AsSpan().SequenceEqual(outcome)).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}
