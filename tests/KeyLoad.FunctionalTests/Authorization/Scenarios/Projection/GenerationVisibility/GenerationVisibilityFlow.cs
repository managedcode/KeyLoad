using KeyLoad.Core.Authorization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class GenerationVisibilityFlow
{
    internal const string Tenant = "tenant";
    internal const string DatabaseScope = "database";
    internal const long OriginalEpoch = 1;
    internal const long RemovedEpoch = 2;
    internal const long RevokedEpoch = 3;
    internal const long RepairedEpoch = 4;
    internal const long InitialRevision = 1;
    internal const long CurrentRevision = 2;
    private const int HeldEntryAndId = 2;
    private const int HeldOwnerEntryIdAndLease = 4;
    private const long NoRetainedBytes = 0;
    private const string MissingCache = "The original shared memory budget refused its visibility owner.";

    internal static PrincipalRecord Principal => new(TeamRowPolicyFlow.Reader, Tenant,
        [new(DatabaseScope, TeamRowPolicyFlow.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
            new(DatabaseScope, TeamGraphVisibilityAssertions.Collection, Capability.DocumentsRead | Capability.Query),
            new(DatabaseScope, TeamGraphVisibilityAssertions.Graph, Capability.GraphRead)], [])
    {
        OwnerId = TeamRowPolicyFlow.Reader,
        RestrictRows = true,
        Teams = [TeamRowPolicyFlow.Team],
        PolicyEpoch = OriginalEpoch
    };

    internal static async Task RunAsync(TestDatabase db, CancellationToken token, bool admissionControls = false)
    {
        using var memory = new CacheMemoryBudget(UnitAdmissionOptions.Cache());
        using var cache = db.Database.ConfigureRowVisibility(memory) ?? throw new InvalidOperationException(MissingCache);
        await SeedAsync(db);
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        if (admissionControls)
        {
            await GenerationVisibilityAdmissionFlow.ControlsAsync(db, queries, cache, memory, token);
        }
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store, InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
        await GenerationVisibilitySnapshotFlow.RunAsync(db, queries, cache, token);
        var writes = await GenerationVisibilityWriteFlow.RunAsync(db, queries, cache, token);
        using var held = GenerationVisibilityAssertions.Hold(db, cache);
        await GenerationVisibilityCrossTenant.RunAsync(db, queries, token);
        var ownerProject = await GenerationVisibilityOwnerProjectFlow.RunAsync(db, queries, cache, token);
        var original = Command(db, InitialRevision, TeamRowPolicyFlow.CurrentJson);
        var receipt = db.Submit(OperationKind.Batch, original, TeamRowPolicyFlow.Reader, original.CommandId).Get<CommitReceipt>();
        await TeamRowPolicyAssertions.ReceiptAsync(db.Store, original, receipt, CurrentRevision, OriginalEpoch);
        var outcome = TeamRowPolicyFlow.Outcome(db.Store, original);
        await TeamRowPolicyFlow.ReplayAsync(db.Database, db.Store, original, receipt, outcome, token);
        await PolicyTransitionsAsync(db, queries, original, outcome, held, token);
        var current = Command(db, CurrentRevision, TeamRowPolicyFlow.HealthyJson);
        var fresh = db.Submit(OperationKind.Batch, current, TeamRowPolicyFlow.Reader, current.CommandId).Get<CommitReceipt>();
        await TeamRowPolicyAssertions.ReceiptAsync(db.Store, current, fresh, TeamRowPolicyFlow.HealthyRevision, RepairedEpoch);
        var currentOutcome = TeamRowPolicyFlow.Outcome(db.Store, current);
        await TeamRowPolicyFlow.ReplayAsync(db.Database, db.Store, current, fresh, currentOutcome, token);
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store, TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson, token);
        cache.Dispose();
        await Assert.That(memory.GetSnapshot().RetainedEntries).IsEqualTo(HeldEntryAndId);
        await Assert.That(memory.GetSnapshot().ActiveReservations).IsEqualTo(HeldOwnerEntryIdAndLease);
        await Assert.That(memory.GetSnapshot().RetainedBytes).IsGreaterThan(NoRetainedBytes);
        await Assert.That(held.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store, TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson, token);
        held.Dispose();
        await Assert.That(memory.GetSnapshot().RetainedBytes).IsEqualTo(NoRetainedBytes);
        await GenerationVisibilityCold.RunAsync(db, memory, original, outcome, current, fresh, currentOutcome, async (database, store, currentToken) =>
        {
            await writes.VerifyAsync(database, store, currentToken);
            await ownerProject.VerifyAsync(database, store, currentToken);
            if (admissionControls)
            {
                await GenerationVisibilityPartialScanFlow.HealthyAsync(database, store, db.Partition,
                TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson,
                    GenerationVisibilityWriteFlow.ResurrectedRevision, currentToken);
            }
        }, token);
    }

    internal static async Task SeedAsync(TestDatabase db)
    {
        db.Configure(TeamRowPolicyFlow.Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row, TeamRowPolicyFlow.OriginalJson, Access: new(TeamId: TeamRowPolicyFlow.Team)),
            new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam, TeamRowPolicyFlow.OriginalJson, Access: new(TeamId: TeamRowPolicyFlow.ForeignTeam)));
        TeamGraphVisibilityAssertions.Seed(db);
        await ConfigureAsync(db, Principal);
    }

    private static async Task PolicyTransitionsAsync(TestDatabase db, QueryEngine queries, CommandRequest original,
        byte[] outcome, RowVisibilityLease held, CancellationToken token)
    {
        await ConfigureAsync(db, Principal with
        {
            Teams = [],
            PolicyEpoch = RemovedEpoch
        });
        await TeamRowPolicyAssertions.HiddenAsync(db, db.Database, queries, db.Store, token);
        await TeamGraphVisibilityAssertions.ReadAsync(db, db.Database, db.Store, db.Partition, false, token);
        await Assert.That(held.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await TeamRowPolicyAssertions.OldRefusedAsync(db.Database, db.Store, original, outcome, ErrorCode.PermissionDenied, token);
        await ConfigureAsync(db, Principal with
        {
            Revoked = true,
            PolicyEpoch = RevokedEpoch
        });
        await TeamRowPolicyAssertions.RevokedAsync(db, queries, db.Store, token);
        await TeamGraphVisibilityAssertions.RevokedAsync(db.Database, db.Store, db.Partition, token);
        await ConfigureAsync(db, Principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store, CurrentRevision, TeamRowPolicyFlow.CurrentJson, token);
        await TeamRowPolicyAssertions.OldRefusedAsync(db.Database, db.Store, original, outcome, ErrorCode.PermissionDenied, token);
    }

    internal static CommandRequest Command(TestDatabase db, long revision, string json)
        => new(Guid.NewGuid(), db.Partition, [new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row, json, revision, new(TeamId: TeamRowPolicyFlow.Team))]);

    internal static async Task ConfigureAsync(TestDatabase db, PrincipalRecord principal)
    {
        var result = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(result).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
}
