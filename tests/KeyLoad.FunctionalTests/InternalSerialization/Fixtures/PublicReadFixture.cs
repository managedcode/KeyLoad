using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal static class PublicReadFixture
{
    internal const string Collection = "orders";
    internal const string Graph = "links";
    internal const string Start = "a";
    internal const string End = "b";
    internal const string Edge = "ab";
    internal const string Label = "next";
    private const string Path = "/@id";
    private const string Alias = "document";
    private const string MissingProjection = "A query projection entry is missing.";
    private const string MissingOrdering = "A query ordering entry is missing.";
    private const string UnsupportedOperand = "The query operand is unsupported.";

    internal static AstQueryRequest Query(TestDatabase database, PublicQueryShape shape)
        => new(database.Partition, shape switch
        {
            PublicQueryShape.Projection => new SelectQuery(Collection, null, [null!], null, [], 1),
            PublicQueryShape.Ordering => new SelectQuery(Collection, null, [new(Path, Alias)], null, [null!], 1),
            PublicQueryShape.InValues => new SelectQuery(Collection, null, [new(Path, Alias)],
                new InPredicate(new FieldOperand(Path), [null!], false), [], 1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        }, AllowFullScan: true);

    internal static string Detail(PublicQueryShape shape) => shape switch
    {
        PublicQueryShape.Projection => MissingProjection,
        PublicQueryShape.Ordering => MissingOrdering,
        PublicQueryShape.InValues => UnsupportedOperand,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static ErrorCode Code(PublicQueryShape shape)
        => shape == PublicQueryShape.InValues ? ErrorCode.UnsupportedCapability : ErrorCode.Validation;

    internal static object Encode<T>(T value, GrainReadKind kind) where T : notnull
    {
        // These are real public MCP JSON arguments; only the internal forwarded value becomes execution.
        var arguments = new Dictionary<string, JsonElement>
        {
            [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options)
        };
        return McpArgumentDecoder.Read<T>(arguments, kind).Payload;
    }

    internal static object Encode(AstQueryRequest query, GrainReadKind kind)
        => kind switch
        {
            GrainReadKind.AstQuery => Encode<AstQueryRequest>(query, kind),
            GrainReadKind.LiveQueryStart => Encode(new StartLiveQueryRequest(query), kind),
            GrainReadKind.LiveQueryRead => Encode(new ReadLiveQueryRequest(query, Alias), kind),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    internal static DecodedGrainRequest Signed(TestDatabase database, object payload, GrainReadKind kind,
        string principal = AuthorityFixture.Root)
    {
        var codec = new GrainRequestCodec(database.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        return codec.Decode(codec.CreateRead(Guid.NewGuid(), principal, kind, payload));
    }

    internal static async Task<object> ExecuteQueryAsync(TestDatabase database, DecodedGrainRequest request)
    {
        var principal = GrainRequestAuthority.Reload(database.Database, request.Envelope.PrincipalId!, TimeProvider.System);
        var capabilities = new GrainQueryReadCapabilities(new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()),
            new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()), TimeProvider.System);
        await using var owner = new RequestWorkOwner();
        object? result = null;
        await KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsBoundCoreOwner.LeaseAsync(database, owner, async (lease, _, _) =>
            result = await capabilities.ExecuteAsync(request.Envelope.ReadKind!.Value, principal.Id, request.Payload, lease,
                CancellationToken.None).ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false);
        return result!;
    }
}
