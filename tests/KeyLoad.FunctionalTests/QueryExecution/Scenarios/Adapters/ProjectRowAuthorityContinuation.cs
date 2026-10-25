using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class ProjectRowAuthorityContinuation
{
    private const string Reader = "alice";
    private const string OtherOwner = "bob";
    private const string Root = "root";
    private const string Collection = "orders";
    private const string DatabaseScope = "database";
    private const string Tenant = "tenant";
    private const string HiddenId = "hidden";
    private const string Project = "row-project-alpha";
    private const string EmptyJson = "{}";
    private const string PutKind = "putDocument";
    private const string MissingOutcome = "The acknowledged original project row repair outcome is absent.";
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;
    private const long ProjectEpoch = 2;
    private const long RemovedEpoch = 3;
    private const long RevokedEpoch = 4;
    private const long RepairedEpoch = 5;

    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        await ProjectRowAuthorityAssertions.ReadAsync(db, engine, db.Store, false, token);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(Collection, HiddenId, EmptyJson, InitialRevision, Access: new(OtherOwner, Project))]);
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(db.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(db.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(db.Database.LastApplied);

        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        MutationReceipt[] mutations = [new(PutKind, Collection, HiddenId, UpdatedRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(mutations))).IsTrue();
        var outcome = Outcome(db.Store, command);
        var principal = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            OwnerId = Reader,
            RestrictRows = true,
            Projects = [Project],
            PolicyEpoch = ProjectEpoch
        };
        await ConfigureAsync(db, principal);
        await ProjectRowAuthorityAssertions.ReadAsync(db, engine, db.Store, true, token);
        await ConfigureAsync(db, principal with
        {
            Projects = [],
            PolicyEpoch = RemovedEpoch
        });
        await ProjectRowAuthorityAssertions.ReadAsync(db, engine, db.Store, false, token);
        await ConfigureAsync(db, principal with
        {
            Revoked = true,
            PolicyEpoch = RevokedEpoch
        });
        await ProjectRowAuthorityAssertions.RefusedAsync(db, engine, db.Store, token);
        await ConfigureAsync(db, principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        await ProjectRowAuthorityAssertions.ReadAsync(db, engine, db.Store, true, token);
        await ReplayAsync(db.Database, db.Store, command, receipt, outcome, token);
        await ProjectRowAuthorityCold.RunAsync(db, command, receipt, outcome, token);
    }

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord expected)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store,
        CommandRequest command, CommitReceipt receipt, byte[] outcome, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var operation = engine.CreateCommand(OperationKind.Batch, command.CommandId, Root, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var replay = engine.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(Outcome(store, command).AsSpan().SequenceEqual(outcome)).IsTrue();
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, Root, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);
}
