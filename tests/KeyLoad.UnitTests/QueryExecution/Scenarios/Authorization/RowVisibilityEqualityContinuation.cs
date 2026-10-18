using ManagedCode.Communication.Commands;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class RowVisibilityEqualityContinuation
{
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;
    private const string MissingOutcome = "The acknowledged original owner-swap outcome is absent.";
    private const string PutKind = "putDocument";
    private const string ExplainId = "explain";
    private const long ExplainRevision = 0;

    internal static async Task RunAsync(TestDatabase db, QueryEngine engine, string owner, string otherOwner,
        bool indexed, bool parameterized)
    {
        QueryRow[] initial = indexed ? [new(EqualityAccessPathSupport.SecondaryId, InitialRevision,
            EqualityAccessPathSupport.SecondaryJson, false, [])] : [];
        await ReadAsync(db, engine, db.Store, owner, indexed, parameterized, initial);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.PrimaryId,
                EqualityAccessPathSupport.PrimaryJson, InitialRevision, Access: new(owner)),
             new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.SecondaryId,
                EqualityAccessPathSupport.SecondaryJson, InitialRevision, Access: new(otherOwner))]);
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(db.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(db.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(db.Store.Position);

        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        MutationReceipt[] mutations = [new(PutKind, EqualityAccessPathSupport.Collection,
            EqualityAccessPathSupport.PrimaryId, UpdatedRevision), new(PutKind, EqualityAccessPathSupport.Collection,
            EqualityAccessPathSupport.SecondaryId, UpdatedRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(mutations))).IsTrue();
        var outcome = Outcome(db.Store, command);
        await HealthyAsync(db, engine, db.Store, owner, indexed, parameterized);
        await ReplayAsync(db.Database, db.Store, command, receipt, outcome);
        await RowVisibilityEqualityCold.RunAsync(db, owner, indexed, parameterized, command, receipt, outcome);
    }

    internal static async Task HealthyAsync(TestDatabase db, QueryEngine engine, ZoneTreeStore store,
        string owner, bool indexed, bool parameterized)
    {
        QueryRow[] rows = [new(EqualityAccessPathSupport.PrimaryId, UpdatedRevision,
            EqualityAccessPathSupport.PrimaryJson, false, [])];
        await ReadAsync(db, engine, store, owner, indexed, parameterized, rows);
    }

    private static async Task ReadAsync(TestDatabase db, QueryEngine engine, ZoneTreeStore store,
        string owner, bool indexed, bool parameterized, QueryRow[] rows)
    {
        var image = GraphShortestPathFullStoreImage.Bytes(store);
        var cut = store.Position;
        var path = indexed ? EqualityAccessPathSupport.IndexPath : EqualityAccessPathSupport.PointPath;
        foreach (var reversed in new[] { false, true })
        {
            var request = EqualityAccessPathSupport.Request(db, indexed, parameterized, reversed);
            var page = engine.Execute(owner, request);
            await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(rows))).IsTrue();
            await PageAsync(page, cut, path);
            var explain = engine.ExecuteAst(owner, new AstQueryRequest(request.Partition,
                new SqlParser(request.Sql, UnitExecutionOptions.DatabaseLimits(db.Database.Limits),
                    UnitExecutionOptions.QueryExecution()).Parse() with
                {
                    Explain = true
                },
                request.Parameters, AllowFullScan: request.AllowFullScan));
            var plan = JsonSerializer.Serialize(new
            {
                AccessPath = path,
                AtomicPartition = db.Partition.AtomicPartitionId,
                ScanBudget = UnitExecutionOptions.DatabaseLimits().Value.MaxScanRecords
            }, JsonDefaults.Options);
            QueryRow[] explanation = [new(ExplainId, ExplainRevision, plan)];
            await Assert.That(JsonDefaults.Serialize(explain.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(explanation))).IsTrue();
            await PageAsync(explain, cut, path);
        }
        await Assert.That(GraphShortestPathFullStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    internal static async Task ReplayAsync(DatabaseEngine engine, ZoneTreeStore store,
        CommandRequest command, CommitReceipt receipt, byte[] outcome)
    {
        var image = GraphShortestPathFullStoreImage.Bytes(store);
        var cut = store.Position;
        var operation = engine.CreateCommand(OperationKind.Batch, command.CommandId, EqualityAccessPathSupport.Root, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var replay = engine.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(Outcome(store, command).AsSpan().SequenceEqual(outcome)).IsTrue();
        await Assert.That(GraphShortestPathFullStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
    {
        return store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition,
            EqualityAccessPathSupport.Root, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);
    }

    private static async Task PageAsync(QueryPage page, long cut, string path)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(path);
    }
}
