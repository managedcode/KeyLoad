using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class QueryCursorExecution
{
    private const int InitialSequence = 0;
    private const string QueryCursorIsInvalidDetail = "The query cursor is invalid.";
    private const int InitialCursorOffset = 0;
    private const string QueryCursorNoLongerHasAValidAuthorizedReadCutDetail = "The query cursor no longer has a valid authorized read cut.";

    /// <summary>The page position: an offset, or for an id-ordered page the last emitted document id.</summary>
    internal readonly record struct State(int Offset, long Cut, long SourceEpoch, ResourceLifetime Lifetime, string? AfterId = null);

    internal static State Resolve(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, string hash, TimeProvider clock, ResourceLifetime lifetime, bool keyset = false)
    {
        var offset = request.Query.Offset;
        string? afterId = null;
        var cut = database.Store.Position;
        var claims = (ReadClaims(database, request.Cursor, lifetime)).Value;
        var sourceEpoch = database.DocumentEpoch(view, request.Partition, request.Query.Collection);
        if (claims is not null)
        {
            if (claims.Incarnation != database.Store.Identity.Incarnation
                || claims.NodeId != database.Store.Identity.NodeId || claims.ReadGeneration != database.Store.Identity.ReadGeneration
                || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch || claims.SchemaVersion != resource.SchemaVersion
                || claims.QueryHash != hash || claims.SourceEpoch != sourceEpoch || claims.CutPosition < InitialSequence || claims.CutPosition > database.Store.Position
                || claims.ExpiresAt <= clock.GetUtcNow()
                || claims.Offset < InitialCursorOffset
                || keyset != (claims.AfterId is not null) || (keyset && claims.Offset != InitialCursorOffset))
            {
                throw Errors.Fail(ErrorCode.CursorExpired, QueryCursorNoLongerHasAValidAuthorizedReadCutDetail);
            }
            offset = claims.Offset;
            afterId = claims.AfterId;
            cut = claims.CutPosition;
        }
        return new(offset, cut, sourceEpoch, lifetime, afterId);
    }

    private static global::ManagedCode.Communication.Result<QueryCursorClaims?> ReadClaims(DatabaseEngine database, string? token, ResourceLifetime lifetime)
    {
        if (token is null)
        {
            return global::ManagedCode.Communication.Result<QueryCursorClaims?>.FailNotFound(nameof(ReadClaims));
        }
        QueryCursorClaims claims;
        try
        {
            claims = database.Verify<QueryCursorClaims>(token);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.TokenInvalidated)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, QueryCursorIsInvalidDetail);
        }
        if (claims.Version != QueryCursorContract.CurrentVersion || claims.Lifetime != lifetime
            || claims.Purpose != QueryCursorContract.Purpose)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, QueryCursorNoLongerHasAValidAuthorizedReadCutDetail);
        }
        return global::ManagedCode.Communication.Result<QueryCursorClaims?>.Succeed(((QueryCursorClaims?)(claims)));
    }

    /// <summary>Signs the next page when eligible rows remain after the next offset. For an id-ordered page the
    /// cursor carries the last emitted document id, and the next page continues after it at offset zero.</summary>
    internal static global::ManagedCode.Communication.Result<string?> SignNext(DatabaseEngine database, TimeSpan cursorLifetime,
        PrincipalRecord principal, ResourceDefinition resource, string hash, State cursor,
        int next, int eligibleCount, TimeProvider clock, string? afterId = null)
        => next < eligibleCount ? global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(database.Sign(new QueryCursorClaims(QueryCursorContract.Purpose, database.Store.Identity.Incarnation,
            database.Store.Identity.NodeId, database.Store.Identity.ReadGeneration, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion, hash, cursor.Cut, cursor.SourceEpoch,
            afterId is null ? next : InitialCursorOffset, clock.GetUtcNow().Add(cursorLifetime), cursor.Lifetime,
            QueryCursorContract.CurrentVersion, afterId))))) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(SignNext));
}

