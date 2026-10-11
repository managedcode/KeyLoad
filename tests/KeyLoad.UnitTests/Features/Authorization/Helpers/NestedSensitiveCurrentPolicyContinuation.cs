using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class NestedSensitiveCurrentPolicyContinuation
{
    internal const string OriginalJson = "{\"people\":[{\"name\":\"A\",\"secret\":\"CANARY\"}],\"odd.name\":\"CANARY\",\"status\":\"open\"}";
    internal const string UpdatedJson = "{\"people\":[{\"name\":\"B\",\"secret\":\"CANARY\"}],\"odd.name\":\"CANARY\",\"status\":\"closed\"}";
    internal const string Reader = "reader";
    internal const string Root = "root";
    internal const string Collection = "orders";
    internal const string DocumentId = "o1";
    internal const string Canary = "CANARY";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string ReadGrant = "pii.read";
    private const string UseGrant = "pii.use";
    private const long FirstRepair = 2;
    private const long RevokedEpoch = 3;
    private const long FinalRepair = 4;
    private const long OriginalRevision = 1;
    private const long CurrentRevision = 2;

    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        var queries = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await NestedSensitiveCurrentPolicyAssertions.ProtectedAsync(db, queries, db.Store, token);
        await PrincipalAsync(db, FirstRepair, false);
        await NestedSensitiveCurrentPolicyAssertions.HealthyAsync(db, db.Database, queries, db.Store,
            OriginalJson, OriginalRevision, token);
        await PrincipalAsync(db, RevokedEpoch, true);
        await NestedSensitiveCurrentPolicyAssertions.RevokedAsync(db, db.Database, queries, db.Store, token);
        await PrincipalAsync(db, FinalRepair, false);
        await NestedSensitiveCurrentPolicyAssertions.HealthyAsync(db, db.Database, queries, db.Store,
            OriginalJson, OriginalRevision, token);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition,
            [new PutDocument(Collection, DocumentId, UpdatedJson, OriginalRevision)]);
        var receipt = db.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        MutationReceipt[] expected = [new("putDocument", Collection, DocumentId, CurrentRevision)];
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Position).IsEqualTo(db.Store.Position);
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await ReplayAsync(db.Database, db.Store, command, receipt, token);
        await NestedSensitiveCurrentPolicyAssertions.HealthyAsync(db, db.Database, queries, db.Store,
            UpdatedJson, CurrentRevision, token);
        await NestedSensitiveCurrentPolicyCold.RunAsync(db, command, receipt, token);
    }

    private static async Task PrincipalAsync(TestDatabase db, long epoch, bool revoked)
    {
        var expected = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.Query)], [ReadGrant, UseGrant])
        { PolicyEpoch = epoch, Revoked = revoked };
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        CommandRequest command, CommitReceipt original, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, Root, default,
            JsonSerializer.Serialize(command, JsonDefaults.Options));
        var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}
