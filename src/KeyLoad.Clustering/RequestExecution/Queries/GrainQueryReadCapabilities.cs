using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.Clustering;

internal sealed class GrainQueryReadCapabilities(QueryEngine queries, SearchEngine search, TimeProvider clock)
{
    internal async ValueTask<GrainValue> ExecuteAsync(GrainReadKind kind, string principal, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == GrainReadKind.WaitForIndex)
        {
            return new GrainValue(await search.WaitForIndexBoundAsync(principal, (GrainPayload.Read<WaitForIndexRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.WaitForIndexRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this WaitForIndex response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.WaitForAnnIndex)
        {
            return new GrainValue(await search.WaitForAnnIndexBoundAsync(principal, (GrainPayload.Read<WaitForAnnIndexRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.WaitForAnnIndexRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this WaitForAnnIndex response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.ApproximateSearch)
        {
            return new GrainValue(await search.ApproximateSearchBoundAsync(principal, (GrainPayload.Read<ApproximateSearchRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.ApproximateSearchRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this ApproximateSearch response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.Search)
        {
            return new GrainValue(await search.SearchBoundAsync(principal, (GrainPayload.Read<SearchRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.SearchRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this Search response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.GraphSearch)
        {
            return new GrainValue(await search.GraphSearchBoundAsync(principal, (GrainPayload.Read<GraphSearchRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.GraphSearchRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this GraphSearch response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.SqlGraphSearch)
        {
            return new GrainValue(await queries.SearchSqlBoundAsync(principal, (GrainPayload.Read<SqlGraphSearchRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.SqlGraphSearchRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this SqlGraphSearch response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.SqlGraphPath)
        {
            return new GrainValue(queries.ShortestPathSqlBound(principal, (GrainPayload.Read<SqlGraphPathRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.SqlGraphPathRequest at this closed operation boundary before executing typed code. */),
                clock, binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this SqlGraphPath response with its generated runtime type header. */;
        }
        return kind switch
        {
            GrainReadKind.Query => new GrainValue(queries.ExecuteBound(principal, (GrainPayload.Read<QueryRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.QueryRequest at this closed operation boundary before executing typed code. */), clock, binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this Query response with its generated runtime type header. */,
            GrainReadKind.AstQuery => new GrainValue(queries.ExecuteAstBound(principal, (GrainPayload.ReadPublicInput<AstQueryRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Query.AstQueryRequest at this closed operation boundary before executing typed code. */), clock, binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this AstQuery response with its generated runtime type header. */,
            GrainReadKind.QueryCapabilities => new GrainValue(Capabilities(request)) /* KLD0041: The current GrainValue wire slot carries this QueryCapabilities response with its generated runtime type header. */,
            GrainReadKind.LiveQueryStart => new GrainValue(queries.StartLiveQueryBound(principal, (GrainPayload.ReadPublicInput<StartLiveQueryRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.StartLiveQueryRequest at this closed operation boundary before executing typed code. */), clock, binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this LiveQueryStart response with its generated runtime type header. */,
            GrainReadKind.LiveQueryRead => new GrainValue(queries.ReadLiveQueryBound(principal, (GrainPayload.ReadPublicInput<ReadLiveQueryRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.ReadLiveQueryRequest at this closed operation boundary before executing typed code. */), clock, binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this LiveQueryRead response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private QueryCapabilityManifest Capabilities(DecodedGrainRequest request)
    {
        GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
        return queries.Capabilities;
    }
}
