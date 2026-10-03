using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativePublicQueryShape
{
    Projection,
    Ordering,
    InValues
}

internal static class NativePublicReadFixture
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

    internal static AstQueryRequest Query(TestDatabase database, NativePublicQueryShape shape)
        => new(database.Partition, shape switch
        {
            NativePublicQueryShape.Projection => new SelectQuery(Collection, null, [null!], null, [], 1),
            NativePublicQueryShape.Ordering => new SelectQuery(Collection, null, [new(Path, Alias)], null, [null!], 1),
            NativePublicQueryShape.InValues => new SelectQuery(Collection, null, [new(Path, Alias)],
                new InPredicate(new FieldOperand(Path), [null!], false), [], 1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        }, AllowFullScan: true);

    internal static string Detail(NativePublicQueryShape shape) => shape switch
    {
        NativePublicQueryShape.Projection => MissingProjection,
        NativePublicQueryShape.Ordering => MissingOrdering,
        NativePublicQueryShape.InValues => UnsupportedOperand,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static ErrorCode Code(NativePublicQueryShape shape)
        => shape == NativePublicQueryShape.InValues ? ErrorCode.UnsupportedCapability : ErrorCode.Validation;

    internal static ReadOnlyMemory<byte> Encode<T>(T value, GrainReadKind kind, TestDatabase database)
    {
        // These are real public MCP JSON arguments; only the internal forwarded value becomes native.
        var arguments = new Dictionary<string, JsonElement>
        { [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options) };
        return McpArgumentDecoder.Read<T>(arguments, kind, database.Database.Limits.MaxQueryBytes).Payload;
    }

    internal static ReadOnlyMemory<byte> Encode(AstQueryRequest query, GrainReadKind kind, TestDatabase database)
        => kind switch
        {
            GrainReadKind.AstQuery => Encode<AstQueryRequest>(query, kind, database),
            GrainReadKind.LiveQueryStart => Encode(new StartLiveQueryRequest(query), kind, database),
            GrainReadKind.LiveQueryRead => Encode(new ReadLiveQueryRequest(query, Alias), kind, database),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    internal static DecodedGrainRequest Signed(TestDatabase database, ReadOnlyMemory<byte> payload, GrainReadKind kind,
        string principal = NativeAuthorityFixture.Root)
    {
        var codec = new GrainRequestCodec(database.Database, TimeProvider.System);
        return codec.Verify(codec.CreateRead(Guid.NewGuid(), principal, kind, payload));
    }

    internal static object ExecuteQuery(TestDatabase database, DecodedGrainRequest request)
    {
        var principal = GrainRequestAuthority.Reload(database.Database, request.Envelope.PrincipalId!, TimeProvider.System);
        return new GrainQueryReadCapabilities(new QueryEngine(database.Database), new SearchEngine(database.Database),
            TimeProvider.System).Execute(request.Envelope.ReadKind!.Value, principal.Id, request.Payload, CancellationToken.None);
    }
}
