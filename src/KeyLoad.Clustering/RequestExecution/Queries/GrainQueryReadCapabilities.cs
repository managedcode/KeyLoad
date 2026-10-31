using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.Clustering;

internal sealed class GrainQueryReadCapabilities(QueryEngine queries, SearchEngine search, TimeProvider clock)
{
    internal async ValueTask<object> ExecuteAsync(GrainReadKind kind, string principal, object payload, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == GrainReadKind.WaitForIndex)
        {
            return await search.WaitForIndexBoundAsync(principal, GrainPayload.Read<WaitForIndexRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.WaitForAnnIndex)
        {
            return await search.WaitForAnnIndexBoundAsync(principal, GrainPayload.Read<WaitForAnnIndexRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.ApproximateSearch)
        {
            return await search.ApproximateSearchBoundAsync(principal, GrainPayload.Read<ApproximateSearchRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.Search)
        {
            return await search.SearchBoundAsync(principal, GrainPayload.Read<SearchRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.GraphSearch)
        {
            return await search.GraphSearchBoundAsync(principal, GrainPayload.Read<GraphSearchRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.SqlGraphSearch)
        {
            return await queries.SearchSqlBoundAsync(principal, GrainPayload.Read<SqlGraphSearchRequest>(payload), binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.SqlGraphPath)
        {
            return queries.ShortestPathSqlBound(principal, GrainPayload.Read<SqlGraphPathRequest>(payload),
                clock, binding, cancellationToken);
        }
        return kind switch
        {
            GrainReadKind.Query => queries.ExecuteBound(principal, GrainPayload.Read<QueryRequest>(payload), clock, binding, cancellationToken),
            GrainReadKind.AstQuery => queries.ExecuteAstBound(principal, GrainPayload.ReadPublicInput<AstQueryRequest>(payload), clock, binding, cancellationToken),
            GrainReadKind.QueryCapabilities => Capabilities(payload),
            GrainReadKind.LiveQueryStart => queries.StartLiveQueryBound(principal, GrainPayload.ReadPublicInput<StartLiveQueryRequest>(payload), clock, binding, cancellationToken),
            GrainReadKind.LiveQueryRead => queries.ReadLiveQueryBound(principal, GrainPayload.ReadPublicInput<ReadLiveQueryRequest>(payload), clock, binding, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private QueryCapabilityManifest Capabilities(object payload)
    {
        GrainPayload.RequireNoDto(payload);
        return queries.Capabilities;
    }
}
