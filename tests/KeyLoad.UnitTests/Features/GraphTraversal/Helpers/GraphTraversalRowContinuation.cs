using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphTraversalRowContinuation
{
    private const string Reader = "alice";
    private const string Root = "root";
    private const string Collection = "orders";
    private const string Graph = "links";
    private const string DatabaseScope = "database";
    private const string Tenant = "tenant";
    private const string Intermediate = "b";
    private const string EmptyJson = "{}";
    private const string MissingOutcome = "The acknowledged original traversal row repair outcome is absent.";
    private const long OriginalRevision = 1;
    private const long UpdatedRevision = 2;
    private const long OriginalOwnershipEpoch = 1;
    private const long ReducedEpoch = 2;
    private const long RevokedEpoch = 3;
    private const long RepairedEpoch = 4;

    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        await GraphTraversalRowAssertions.ReadAsync(db.Database, db.Store, db.Partition, false, token);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(Collection, Intermediate, EmptyJson, OriginalRevision, Access: new(Reader))]);
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(db.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(db.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(db.Store.Position);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(OriginalOwnershipEpoch);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        MutationReceipt[] mutations = [new("putDocument", Collection, Intermediate, UpdatedRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(mutations))).IsTrue();
        var outcome = Outcome(db.Store, command);
        await GraphTraversalRowAssertions.ReadAsync(db.Database, db.Store, db.Partition, true, token);
        var principal = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead), new(DatabaseScope, Graph, Capability.GraphRead)], [])
        { OwnerId = Reader, RestrictRows = true, PolicyEpoch = RepairedEpoch };
        await ConfigureAsync(db, principal with
        {
            PolicyEpoch = ReducedEpoch,
            Grants = [new(DatabaseScope, Collection, Capability.DocumentsRead)]
        });
        await GraphTraversalRowAssertions.RefusedAsync(db.Database, db.Store, db.Partition, ErrorCode.PermissionDenied, token);
        await ConfigureAsync(db, principal with { PolicyEpoch = RevokedEpoch, Revoked = true });
        await GraphTraversalRowAssertions.RefusedAsync(db.Database, db.Store, db.Partition, ErrorCode.Unauthenticated, token);
        await ConfigureAsync(db, principal);
        await GraphTraversalRowAssertions.ReadAsync(db.Database, db.Store, db.Partition, true, token);
        await ReplayAsync(db.Database, db.Store, command, receipt, outcome, token);
        await GraphTraversalRowCold.RunAsync(db, command, receipt, outcome, token);
    }

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord expected)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store,
        CommandRequest command, CommitReceipt receipt, byte[] outcome, CancellationToken token)
    {
        var image = GraphShortestPathFullStoreImage.Bytes(store);
        var cut = store.Position;
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, Root, default,
            JsonSerializer.Serialize(command, JsonDefaults.Options));
        var replay = engine.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(Outcome(store, command).AsSpan().SequenceEqual(outcome)).IsTrue();
        await Assert.That(GraphShortestPathFullStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, Root, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);
}
