using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Orleans;

internal sealed class GrainQueryReadCapabilities(QueryEngine queries, SearchEngine search, TimeProvider clock,
    PhysicalShardRecord expectedOwner)
{
    internal async ValueTask<object> ExecuteAsync(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == GrainReadKind.PartitionQueryLeaf)
        {
            return PartitionQueryLeafExecution.Execute(queries, principal,
                GrainNativePayload.Read<PartitionQueryOwnedLeafRequest>(payload), expectedOwner, clock, cancellationToken);
        }
        if (kind == GrainReadKind.PartitionQuery)
        {
            return queries.QueryPartitions(principal,
                GrainNativePayload.ReadPublicInput<PartitionQueryRequestV1>(payload), expectedOwner, clock,
                cancellationToken);
        }
        if (kind == GrainReadKind.WaitForIndex)
        {
            return await search.WaitForIndexAsync(principal, GrainNativePayload.Read<WaitForIndexRequest>(payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.ApproximateSearch)
        {
            return await search.ApproximateSearchAsync(principal, GrainNativePayload.Read<ApproximateSearchRequest>(payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.Search)
        {
            return await search.SearchAsync(principal, GrainNativePayload.Read<SearchRequest>(payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.GraphSearch)
        {
            return await search.GraphSearchAsync(principal, GrainNativePayload.Read<GraphSearchRequest>(payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.SqlGraphSearch)
        {
            return await queries.SearchSqlAsync(principal, GrainNativePayload.Read<SqlGraphSearchRequest>(payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.SqlGraphPath)
        {
            return queries.ShortestPathSql(principal, GrainNativePayload.Read<SqlGraphPathRequest>(payload),
                clock, cancellationToken);
        }
        return kind switch
        {
            GrainReadKind.Query => queries.Execute(principal, GrainNativePayload.Read<QueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.AstQuery => queries.ExecuteAst(principal, GrainNativePayload.ReadPublicInput<AstQueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.QueryCapabilities => Capabilities(payload),
            GrainReadKind.LiveQueryStart => queries.StartLiveQuery(principal, GrainNativePayload.ReadPublicInput<StartLiveQueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.LiveQueryRead => queries.ReadLiveQuery(principal, GrainNativePayload.ReadPublicInput<ReadLiveQueryRequest>(payload), clock, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private QueryCapabilityManifest Capabilities(ReadOnlyMemory<byte> payload)
    {
        GrainNativePayload.RequireNoDto(payload);
        return queries.Capabilities;
    }
}
