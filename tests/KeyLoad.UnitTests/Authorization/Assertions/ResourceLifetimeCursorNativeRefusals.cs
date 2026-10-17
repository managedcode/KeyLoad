using KeyLoad.Query;
using KeyLoad.Query.Features.ChangeFeeds;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeCursorNativeRefusals
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, QueryEngine engine, string sql,
        AstQueryRequest liveQuery, QueryCursorClaims scalar, LiveCursorClaims live, ResourceLifetimeV1 foreign)
    {
        var scalarOld = new ResourceLifetimeScalarCursorNegativeV1(ResourceLifetimeCursorNegativeProtocol.ScalarPurpose,
            scalar.Incarnation, scalar.NodeId, scalar.ReadGeneration, scalar.PrincipalId, scalar.PolicyEpoch,
            scalar.SchemaVersion, scalar.QueryHash, scalar.CutPosition, scalar.SourceEpoch, scalar.Offset, scalar.ExpiresAt);
        var liveOld = new ResourceLifetimeLiveCursorNegativeV1(ResourceLifetimeCursorNegativeProtocol.LivePurpose,
            live.QueryHash, live.ChangeCursor);
        foreach (var cursor in new[] { owner.Database.Sign(scalarOld), owner.Database.Sign(scalar with { Lifetime = foreign }) })
        {
            await RefuseAsync(owner, () => engine.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, sql, AllowFullScan: true, Cursor: cursor),
                cancellationToken: owner.CallerToken), ErrorCode.CursorExpired);
        }
        foreach (var cursor in new[] { owner.Database.Sign(liveOld), owner.Database.Sign(live with { Lifetime = foreign }) })
        {
            await RefuseAsync(owner, () => engine.ReadLiveQuery(SafeProjectorOperationProtocol.Reader,
                new(liveQuery, cursor), cancellationToken: owner.CallerToken), ErrorCode.TokenInvalidated);
        }
    }

    internal static async Task CurrentAsync(SafeProjectorOperationDatabase owner, QueryEngine engine, string sql,
        AstQueryRequest liveQuery, string scalar, string live)
    {
        await RefuseAsync(owner, () => engine.Execute(SafeProjectorOperationProtocol.Reader,
            new(owner.Source.Partition, sql, AllowFullScan: true, Cursor: scalar),
            cancellationToken: owner.CallerToken), ErrorCode.CursorExpired);
        await RefuseAsync(owner, () => engine.ReadLiveQuery(SafeProjectorOperationProtocol.Reader,
            new(liveQuery, live), cancellationToken: owner.CallerToken), ErrorCode.TokenInvalidated);
    }

    private static async Task RefuseAsync<T>(SafeProjectorOperationDatabase owner, Func<T> execute, ErrorCode expected)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var before = owner.Store.GetReadDiagnostics();
            var error = Assert.ThrowsExactly<KeyLoadException>(() => { _ = execute(); });
            await Assert.That(error.Code).IsEqualTo(expected);
            await Assert.That(owner.Store.GetReadDiagnostics().RangeExaminedBytes).IsEqualTo(before.RangeExaminedBytes);
        });
    }
}
