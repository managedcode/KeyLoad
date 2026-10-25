using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationCursor
{
    private const string Purpose = "query-page";
    private const int CursorVersion = 2;
    private const string FullScan = "bounded-full-scan";
    private const string MissingCursor = "The real safe projector bounded query cursor is absent.";
    private const string InvalidQueryCursor = "The query cursor no longer has a valid authorized read cut.";
    private const string InvalidFeedCursor = "The change-feed cursor requires a fresh authorized snapshot.";
    private const long ResourceSchema = 1;
    private const int IdContinuationOffset = 0;

    internal static async Task<string> CaptureAsync(SafeProjectorOperationDatabase owner, long policyEpoch,
        long documentEpoch, string json, long revision, bool raw, CancellationToken token)
    {
        string? cursor = null;
        var query = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var admitted = ExpectedLifetime(owner);
            var cut = admitted.Cut;
            var before = owner.Database.EvaluationClock.GetUtcNow();
            var page = query.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationRequests.PageSql, AllowFullScan: true), cancellationToken: token);
            var after = owner.Database.EvaluationClock.GetUtcNow();
            var actualCursor = page.Cursor ?? throw new InvalidOperationException(MissingCursor);
            var claims = owner.Database.Verify<QueryCursorClaims>(actualCursor);
            var lifetime = UnitExecutionOptions.QueryExecution().Value.CursorLifetime;
            await Assert.That(claims.ExpiresAt >= before.Add(lifetime) && claims.ExpiresAt <= after.Add(lifetime)).IsTrue();
            var expected = new QueryCursorClaims(Purpose, owner.Store.Identity.Incarnation, owner.Store.Identity.NodeId,
                owner.Store.Identity.ReadGeneration, SafeProjectorOperationProtocol.Reader, policyEpoch, ResourceSchema,
                QueryEngine.QueryHash(PageRequest(owner)), cut, documentEpoch, IdContinuationOffset, claims.ExpiresAt, admitted.Lifetime,
                CursorVersion, SafeProjectorOperationProtocol.First);
            await SafeProjectorOperationSeed.EqualAsync(claims, expected);
            var paths = raw ? ImmutableArray<string>.Empty : SafeProjectorOperationProtocol.RedactedPaths;
            await SafeProjectorOperationSeed.EqualAsync(page, new QueryPage(
                [new(SafeProjectorOperationProtocol.First, revision, json, !raw, paths)], owner.Database.Sign(expected), cut, FullScan));
            cursor = actualCursor;
        });
        var original = cursor ?? throw new InvalidOperationException(MissingCursor);
        await FollowingAsync(owner, query, original, raw, token);
        return original;
    }

    internal static async Task RefuseAsync(SafeProjectorOperationDatabase owner, string queryCursor,
        string feedCursor, CancellationToken token)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var query = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => query.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationRequests.PageSql, AllowFullScan: true, Cursor: queryCursor),
                cancellationToken: token));
            await ErrorAsync(actual, ErrorCode.CursorExpired, InvalidQueryCursor);
        });
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.ReadChangeFeed(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, Cursor: feedCursor), token));
            await ErrorAsync(actual, ErrorCode.TokenInvalidated, InvalidFeedCursor);
        });
    }

    private static async Task FollowingAsync(SafeProjectorOperationDatabase owner, QueryEngine query,
        string cursor, bool raw, CancellationToken token)
        => await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var cut = owner.Store.Position;
            var page = query.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationRequests.PageSql, AllowFullScan: true, Cursor: cursor), cancellationToken: token);
            var paths = raw ? ImmutableArray<string>.Empty : SafeProjectorOperationProtocol.RedactedPaths;
            await SafeProjectorOperationSeed.EqualAsync(page, new QueryPage(
                [new(SafeProjectorOperationProtocol.Second, SafeProjectorOperationProtocol.InitialRevision,
                    SafeProjectorOperationProtocol.SecondJson, !raw, paths)], null, cut, FullScan));
        });

    private static (long Cut, ResourceLifetime Lifetime) ExpectedLifetime(SafeProjectorOperationDatabase owner)
        => owner.Store.Read(view =>
        {
            var partition = owner.Source.Partition;
            var principal = owner.Database.Principal(view, SafeProjectorOperationProtocol.Reader,
                owner.Database.EvaluationClock.GetUtcNow());
            owner.Database.Authorization.Require(principal, partition, SafeProjectorOperationProtocol.Collection,
                Capability.Query | Capability.DocumentsRead);
            var resource = owner.Database.Resource(view, partition, SafeProjectorOperationProtocol.Collection,
                ResourceKind.Collection);
            var lifetime = (ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                resource.Name, resource)).Value ?? throw Errors.Fail(ErrorCode.Corruption, MissingCursor);
            return (owner.Store.Position, lifetime);
        });

    private static AstQueryRequest PageRequest(SafeProjectorOperationDatabase owner)
        => new(owner.Source.Partition, new(SafeProjectorOperationProtocol.Collection, null,
            [new(SafeProjectorOperationProtocol.Star, SafeProjectorOperationProtocol.Star)], null,
            [new(SafeProjectorOperationProtocol.IdPath, false)], SafeProjectorOperationProtocol.SingleRow), AllowFullScan: true);

    private static async Task ErrorAsync(KeyLoadException actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.Code).IsEqualTo(code);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Message, detail)).IsTrue();
        await Assert.That(actual.Message?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
    }
}
