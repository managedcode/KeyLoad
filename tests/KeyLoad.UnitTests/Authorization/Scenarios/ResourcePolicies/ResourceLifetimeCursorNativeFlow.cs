using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.Features.ChangeFeeds;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeCursorNativeFlow
{
    private const int Version = 2;
    private const int ColdPasses = 2;
    private const int First = 0;
    private const string StatusAlias = "status";
    private const string ScalarPurpose = "query-page-v2";
    private const string LivePurpose = "scalar-live-query-v2";
    private const string PageSql = "SELECT TOP (1) status FROM [safe-projector-operations] ORDER BY id";
    private const string FirstJson = "{\"status\":\"open\"}";
    private const string SecondJson = "{\"status\":\"second\"}";

    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationState state,
        SafeProjectorOperationState foreignState)
    {
        var partition = owner.Source.Partition;
        var expected = ResourceLifetimeIdentity.Create(state.ResourceCommand.Operation,
            partition.TenantId, partition.DatabaseId, state.Resource);
        var foreign = ResourceLifetimeIdentity.Create(foreignState.ResourceCommand.Operation,
            partition.TenantId, partition.DatabaseId, foreignState.Resource);
        await Assert.That(expected.Incarnation).IsNotEqualTo(foreign.Incarnation);
        await RoundAsync(owner, expected, foreign);
        for (var pass = First; pass < ColdPasses; pass++)
        {
            await owner.ReopenAsync();
            await RoundAsync(owner, expected, foreign);
            await SafeProjectorOperationSeed.EqualAsync(owner.Submit(state.Seed.Operation).Get<CommitReceipt>(),
                state.Seed.Result.Get<CommitReceipt>());
        }
    }

    private static async Task RoundAsync(SafeProjectorOperationDatabase owner, ResourceLifetimeV1 expected,
        ResourceLifetimeV1 foreign)
    {
        var token = owner.CallerToken;
        var engine = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        var page = engine.Execute(SafeProjectorOperationProtocol.Reader,
            new(owner.Source.Partition, PageSql, AllowFullScan: true), cancellationToken: token);
        await SafeProjectorOperationSeed.EqualAsync(page.Rows,
            System.Collections.Immutable.ImmutableArray.Create(new QueryRow(SafeProjectorOperationProtocol.First,
                SafeProjectorOperationProtocol.InitialRevision, FirstJson, false, [])));
        var scalar = owner.Database.Verify<QueryCursorClaims>(page.Cursor ?? throw new InvalidOperationException());
        await SafeProjectorOperationSeed.EqualAsync(scalar.Lifetime, expected);
        await Assert.That(scalar.Version).IsEqualTo(Version);
        await Assert.That(scalar.Purpose).IsEqualTo(ScalarPurpose);
        var liveQuery = Live(owner);
        var snapshot = engine.StartLiveQuery(SafeProjectorOperationProtocol.Reader, new(liveQuery), cancellationToken: token);
        await SafeProjectorOperationSeed.EqualAsync(snapshot.Rows.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray(),
            new[] { new QueryRow(SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.InitialRevision,
                FirstJson, false, []), new QueryRow(SafeProjectorOperationProtocol.Second,
                SafeProjectorOperationProtocol.InitialRevision, SecondJson, false, []) });
        var live = owner.Database.Verify<LiveCursorClaims>(snapshot.Cursor);
        await SafeProjectorOperationSeed.EqualAsync(live.Lifetime, expected);
        await Assert.That(live.Version).IsEqualTo(Version);
        await Assert.That(live.Purpose).IsEqualTo(LivePurpose);
        await ResourceLifetimeCursorNativeRefusals.RunAsync(owner, engine, PageSql, liveQuery, scalar, live, foreign);
        await ResourceLifetimeCursorNativeDrift.RunAsync(owner, engine, PageSql, liveQuery,
            page.Cursor!, snapshot.Cursor, foreign);
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var following = engine.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, PageSql, AllowFullScan: true, Cursor: page.Cursor), cancellationToken: token);
            await SafeProjectorOperationSeed.EqualAsync(following.Rows,
                System.Collections.Immutable.ImmutableArray.Create(new QueryRow(SafeProjectorOperationProtocol.Second,
                    SafeProjectorOperationProtocol.InitialRevision, SecondJson, false, [])));
            await Assert.That(following.Cursor).IsNull();
            var changes = engine.ReadLiveQuery(SafeProjectorOperationProtocol.Reader,
                new(liveQuery, snapshot.Cursor), cancellationToken: token);
            await Assert.That(changes.Changes).IsEmpty();
            await Assert.That(changes.HasMore).IsFalse();
        });
    }

    private static AstQueryRequest Live(SafeProjectorOperationDatabase owner)
        => new(owner.Source.Partition, new(SafeProjectorOperationProtocol.Collection, null,
            [new(SafeProjectorOperationProtocol.StatusPath, StatusAlias)], null, [],
            SafeProjectorOperationProtocol.BothRows), AllowFullScan: true);
}
