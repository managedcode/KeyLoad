using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class GenerationVisibilityWriteFlow
{
    internal const long VisibleRevision = 2;
    internal const long TombstoneRevision = 3;
    internal const long ResurrectedRevision = 4;
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";
    private readonly List<(CommandRequest Command, CommitReceipt Receipt, byte[] Outcome)> original = [];

    internal static async Task<GenerationVisibilityWriteFlow> RunAsync(TestDatabase db, QueryEngine queries,
        RowVisibilityCache cache, CancellationToken token)
    {
        var flow = new GenerationVisibilityWriteFlow();
        using var negative = GenerationVisibilityAssertions.Hold(db, cache);
        await Assert.That(negative.Contains(TeamRowPolicyFlow.ForeignTeam)).IsFalse();
        await flow.ApplyAsync(db, new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam,
            TeamRowPolicyFlow.OriginalJson, GenerationVisibilityFlow.InitialRevision, new(TeamId: TeamRowPolicyFlow.Team)), PutKind, VisibleRevision, token);
        await GenerationVisibilityWriteAssertions.RowsAsync(db, db.Database, queries, db.Store,
            GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, true, token);
        await Assert.That(negative.Contains(TeamRowPolicyFlow.ForeignTeam)).IsFalse();
        using var positive = GenerationVisibilityAssertions.Hold(db, cache);
        await Assert.That(positive.Contains(TeamRowPolicyFlow.ForeignTeam)).IsTrue();
        await flow.ApplyAsync(db, new DeleteDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam,
            VisibleRevision), DeleteKind, TombstoneRevision, token);
        await GenerationVisibilityWriteAssertions.RowsAsync(db, db.Database, queries, db.Store,
            GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, false, token);
        await Assert.That(positive.Contains(TeamRowPolicyFlow.ForeignTeam)).IsTrue();
        await flow.ApplyAsync(db, new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.ForeignTeam,
            TeamRowPolicyFlow.OriginalJson, TombstoneRevision, new(TeamId: TeamRowPolicyFlow.ForeignTeam)), PutKind, ResurrectedRevision, token);
        await GenerationVisibilityWriteAssertions.RowsAsync(db, db.Database, queries, db.Store,
            GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, false, token);
        await Assert.That(positive.Contains(TeamRowPolicyFlow.ForeignTeam)).IsTrue();
        await flow.ReplayAsync(db.Database, db.Store, token);
        return flow;
    }

    private async Task ApplyAsync(TestDatabase db, Mutation mutation, string kind, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, [mutation]);
        var before = db.Database.EvaluationClock.GetUtcNow();
        var receipt = db.Submit(OperationKind.Batch, command, TeamRowPolicyFlow.Root, command.CommandId).Get<CommitReceipt>();
        var after = db.Database.EvaluationClock.GetUtcNow();
        await GenerationVisibilityWriteAssertions.ReceiptAsync(db, command, receipt, kind, revision);
        await GenerationVisibilityWriteAssertions.RecordAsync(db.Store, db.Partition, revision,
            revision == TombstoneRevision, revision == ResurrectedRevision ? TeamRowPolicyFlow.ForeignTeam : TeamRowPolicyFlow.Team, before, after);
        var outcome = db.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(db.Partition, TeamRowPolicyFlow.Root, command.CommandId)))!;
        original.Add((command, receipt, outcome));
        await ReplayAsync(db.Database, db.Store, token);
    }

    private async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        foreach (var item in original)
        {
            var operation = TeamRowPolicyFlow.Operation(item.Command) with { PrincipalId = TeamRowPolicyFlow.Root };
            var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(item.Receipt))).IsTrue();
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(item.Command.Partition,
                TeamRowPolicyFlow.Root, item.Command.CommandId)))!.AsSpan().SequenceEqual(item.Outcome)).IsTrue();
        }
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal async Task VerifyAsync(DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        await ReplayAsync(database, store, token);
        var partition = original.First().Command.Partition;
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await GenerationVisibilityWriteAssertions.ColdRowsAsync(database, queries, store, partition, token);
    }
}
