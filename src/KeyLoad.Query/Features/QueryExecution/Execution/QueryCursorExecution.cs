using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class QueryCursorExecution
{
    private const int InitialSequence = 0;
    private const int FirstElementIndex = 0;
    private const string QueryCursorIsInvalidDetail = "The query cursor is invalid.";
    private const int InitialCursorOffset = 0;
    private const string QueryCursorNoLongerHasAValidAuthorizedReadCutDetail = "The query cursor no longer has a valid authorized read cut.";
    private const string CursorPurpose = "query-page";

    internal readonly record struct State(int Offset, long Cut, long SourceEpoch);

    internal static State Resolve(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, string hash, TimeProvider clock)
    {
        var offset = FirstElementIndex;
        var cut = database.Store.Position;
        var sourceEpoch = database.DocumentEpoch(view, request.Partition, request.Query.Collection);
        if (request.Cursor is { } cursor)
        {
            QueryCursorClaims claims;
            try
            { claims = database.Verify<QueryCursorClaims>(cursor); }
            catch (KeyLoadException exception) when (exception.Code == ErrorCode.TokenInvalidated)
            { throw Errors.Fail(ErrorCode.CursorExpired, QueryCursorIsInvalidDetail); }
            if (claims.Purpose != CursorPurpose || claims.Incarnation != database.Store.Identity.Incarnation
                || claims.NodeId != database.Store.Identity.NodeId || claims.ReadGeneration != database.Store.Identity.ReadGeneration
                || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch || claims.SchemaVersion != resource.SchemaVersion
                || claims.QueryHash != hash || claims.SourceEpoch != sourceEpoch || claims.CutPosition < InitialSequence || claims.CutPosition > database.Store.Position
                || claims.ExpiresAt < clock.GetUtcNow()
                || claims.Offset < InitialCursorOffset || claims.Offset > database.Limits.MaxScanRecords)
            {
                throw Errors.Fail(ErrorCode.CursorExpired, QueryCursorNoLongerHasAValidAuthorizedReadCutDetail);
            }
            offset = claims.Offset;
            cut = claims.CutPosition;
        }
        return new(offset, cut, sourceEpoch);
    }

    internal static string? SignNext(DatabaseEngine database, TimeSpan cursorLifetime,
        PrincipalRecord principal, ResourceDefinition resource, string hash, State cursor,
        int next, int eligibleCount, TimeProvider clock)
        => next < eligibleCount ? database.Sign(new QueryCursorClaims(CursorPurpose, database.Store.Identity.Incarnation,
            database.Store.Identity.NodeId, database.Store.Identity.ReadGeneration, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion, hash, cursor.Cut, cursor.SourceEpoch, next,
            clock.GetUtcNow().Add(cursorLifetime))) : null;
}

