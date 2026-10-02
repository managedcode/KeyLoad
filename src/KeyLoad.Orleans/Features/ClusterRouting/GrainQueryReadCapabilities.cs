using KeyLoad.Query;

namespace KeyLoad.Orleans;

internal sealed class GrainQueryReadCapabilities(QueryEngine queries, SearchEngine search, TimeProvider clock)
{
    internal object Execute(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Query => queries.Execute(principal, GrainPayloadJson.Read<QueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.AstQuery => queries.ExecuteAst(principal, GrainPayloadJson.Read<AstQueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.QueryCapabilities => Capabilities(payload),
            GrainReadKind.LiveQueryStart => queries.StartLiveQuery(principal, GrainPayloadJson.Read<StartLiveQueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.LiveQueryRead => queries.ReadLiveQuery(principal, GrainPayloadJson.Read<ReadLiveQueryRequest>(payload), clock, cancellationToken),
            GrainReadKind.Search => search.Search(principal, GrainPayloadJson.Read<SearchRequest>(payload), cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private QueryCapabilityManifest Capabilities(ReadOnlyMemory<byte> payload)
    {
        GrainPayloadJson.RequireNull(payload);
        return queries.Capabilities;
    }
}
