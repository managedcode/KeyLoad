using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredAssertions
{
    internal static async Task ReplayAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
        {
            await PlacementAsync(source, target, state, token);
            if (state.EpochFenced)
            { await FencedAsync(source, state, SqlTypedInsertLiteral.EpochChanged, token); }
            else
            {
                await SqlTypedInsertAssertions.SameAsync(state.Receipt, source.Database.ResolveOutcome(state.Original).Get<CommitReceipt>());
                await SqlTypedInsertAssertions.SameAsync(state.Receipt, source.Journal.Submit(state.Original, token).Get<CommitReceipt>());
                var work = new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token);
                await SqlTypedInsertAssertions.SameAsync(state.Receipt, source.Database.ResolveControlledDocumentOutcome(
                    SqlTypedInsertRetiredLiteral.Writer, state.Original, work).Get<CommitReceipt>());
                await SqlTypedInsertAssertions.SameAsync(new OperationResult(null, ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch),
                    source.Database.ResolveOutcome(state.Failed));
            }
            if (state.Healthy is { } healthy)
            { await SqlTypedInsertAssertions.SameAsync(state.HealthyReceipt, target.Journal.Submit(healthy, token).Get<CommitReceipt>()); }
        }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    }
    internal static async Task FencedAsync(ControlledPartitionMovementNode source, SqlTypedInsertRetiredState state,
        string detail, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = new OperationResult(null, ErrorCode.PermissionDenied, detail);
        await SqlTypedInsertAssertions.SameAsync(expected, source.Database.ResolveOutcome(state.Original));
        await SqlTypedInsertAssertions.SameAsync(expected, source.Database.ResolveOutcome(state.Failed));
    }
    internal static async Task RowsAsync(ControlledPartitionMovementNode target, bool repaired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var partition = ControlledPartitionMovementCorpus.Partition;
        var image = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var cut = target.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlTypedInsertAssertions.SameAsync(new DocumentResult(new(partition, SqlTypedInsertRetiredLiteral.Table,
                SqlTypedInsertRetiredLiteral.One), SqlTypedInsertRetiredLiteral.Revision, SqlTypedInsertRetiredLiteral.OneJson, false, []),
                target.Database.GetDocument(SqlTypedInsertRetiredLiteral.Writer, new(partition, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.One), cancellationToken: token));
            if (repaired)
            {
                await SqlTypedInsertAssertions.SameAsync(new DocumentResult(new(partition, SqlTypedInsertRetiredLiteral.Table,
                    SqlTypedInsertRetiredLiteral.Two), SqlTypedInsertRetiredLiteral.Revision, SqlTypedInsertRetiredLiteral.TwoJson, false, []),
                    target.Database.GetDocument(SqlTypedInsertRetiredLiteral.Writer, new(partition, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.Two), cancellationToken: token));
            }
            else
            {
                await Assert.That(target.Database.GetDocument(SqlTypedInsertRetiredLiteral.Writer,
                new(partition, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.Two), cancellationToken: token)).IsNull();
            }
            var rows = new List<QueryRow> { new(SqlTypedInsertRetiredLiteral.One, SqlTypedInsertRetiredLiteral.Revision, OneProjection, false, []) };
            if (repaired)
            { rows.Add(new(SqlTypedInsertRetiredLiteral.Two, SqlTypedInsertRetiredLiteral.Revision, TwoProjection, false, [])); }
            var expected = new QueryPage(global::System.Collections.Immutable.ImmutableArray.CreateRange(rows), null, cut, FullScan);
            var engine = new QueryEngine(target.Database, UnitExecutionOptions.QueryExecution());
            await SqlTypedInsertAssertions.SameAsync(expected, engine.Execute(SqlTypedInsertRetiredLiteral.Writer,
                new(partition, SqlTypedInsertRetiredLiteral.ReadSql, AllowFullScan: true), cancellationToken: token));
            await SqlTypedInsertAssertions.SameAsync(expected, engine.ExecuteAst(SqlTypedInsertRetiredLiteral.Writer,
                new(partition, new SelectQuery(SqlTypedInsertRetiredLiteral.Table, null,
                    [new(SqlTypedInsertRetiredLiteral.TitlePath, SqlTypedInsertRetiredLiteral.Title)], null,
                    [new(SqlTypedInsertRetiredLiteral.IdPath, false)], SqlTypedInsertRetiredLiteral.MaximumRows), AllowFullScan: true), cancellationToken: token));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlTypedInsertAssertions.UnchangedAsync(target.Store, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task PlacementAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        var placement = source.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            new(SqlTypedInsertRetiredLiteral.Version, ControlledPartitionMovementCorpus.Partition));
        await Assert.That(placement.PhysicalShardId).IsEqualTo(target.PhysicalOwner.PhysicalShardId);
        await Assert.That(placement.Incarnation).IsEqualTo(target.PhysicalOwner.Incarnation);
        await Assert.That(placement.VoterIds.SequenceEqual(target.PhysicalOwner.VoterIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(placement.PlacementEpoch).IsEqualTo(SqlTypedInsertRetiredLiteral.PublishedEpoch);
        await Assert.That(state.Receipt.Token.Incarnation).IsEqualTo(source.PhysicalOwner.Incarnation);
        await Assert.That(state.Receipt.Token.OwnershipEpoch).IsEqualTo(SqlTypedInsertRetiredLiteral.OriginalEpoch);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.GetDocument(SqlTypedInsertRetiredLiteral.Writer,
            new(ControlledPartitionMovementCorpus.Partition, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.One), cancellationToken: token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }
    private const string OneProjection = "{\"title\":\"before-move\"}";
    private const string TwoProjection = "{\"title\":\"after-repair\"}";
    private const string FullScan = "bounded-full-scan";
}
