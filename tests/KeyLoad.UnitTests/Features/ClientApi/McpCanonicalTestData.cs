using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Actual public contract instances for public JSON, native payload and stable-identity assertions.</summary>
internal static class McpCanonicalTestData
{
    internal const string RequestKey = "request";
    internal const string CommandKey = "commandId";
    internal const string ReceiveKey = "requestId";
    internal const string UnknownKey = "principalId";
    internal const string NullJson = "null";
    internal const string EmptyJson = "{}";
    internal const string StableIdText = "ee5cf37e-0e81-4a94-8725-021e30f7b721";
    internal const string Tenant = "mcp-tenant";
    internal const string Database = "mcp-database";
    internal const string Domain = "mcp-domain";
    internal const string PartitionKey = "mcp-partition";
    internal const string Resource = "mcp-records";
    internal const string Entity = "mcp-entity";
    internal const string Principal = "mcp-principal";
    internal const string Field = "name";
    internal const string Cursor = "bounded-cursor";
    private const string Token = "delivery-token";
    private const string Handler = "handler-scope";
    private const string ApiKey = "key-record";
    private const string Verifier = "persisted-verifier";
    private const string Sql = "SELECT * FROM mcp-records";
    private const long Generation = 1;
    private const long Position = 0;
    private const int Limit = 10;
    internal static readonly Guid StableId = Guid.Parse(StableIdText);
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static readonly EntityRef Reference = new(Partition, Resource, Entity);
    internal static readonly QueueLaneRef Lane = new(Partition, Resource);
    internal static readonly EventSourceRef Source = new(Partition, Resource, EventSourceKind.Topic);
    internal static readonly SubscriptionRef Subscription = new(Source, Entity);
    internal static readonly ProjectionConsumerRef Consumer = new(Partition, Resource);

    internal static AstQueryRequest Ast() => new(Partition,
        new SelectQuery(Resource, null, [new Selection(Field, Field)], null, [], Limit));

    internal static ImmutableArray<McpDecodeCase> Commands() =>
    [
        Case(McpCatalogExpectations.DocumentsCommit, new CommandRequest(StableId, Partition, Effects()), CommandKey),
        Case(McpCatalogExpectations.MessagesReceive, new ReceiveRequest(StableId, Lane), ReceiveKey),
        Case(McpCatalogExpectations.MessagesComplete, new DeliveryCommand(StableId, Lane, Token, DeliveryAction.Ack), CommandKey),
        Case(McpCatalogExpectations.MessagesProcess, new ProcessingRequest(StableId, Lane, Token, Handler, Generation, Effects()), CommandKey),
        Case(McpCatalogExpectations.ResourcesConfigure, new ConfigureResourceRequest(Tenant, Database,
            new ResourceDefinition(Resource, ResourceKind.Collection, Domain)), null),
        Case(McpCatalogExpectations.PrincipalsConfigure, new ConfigurePrincipalRequest(new PrincipalRecord(Principal, Tenant, [], [])), null),
        Case(McpCatalogExpectations.CredentialsConfigure, new ConfigureApiKeyRequest(new ApiKeyRecord(ApiKey, Principal, Verifier)), null),
        Case(McpCatalogExpectations.AdminDispatch, true, null),
        Case(McpCatalogExpectations.SubscriptionsConfigure,
            new ConfigureSubscriptionRequest(StableId, Subscription, new SubscriptionDefinition(Principal)), CommandKey),
        Case(McpCatalogExpectations.SubscriptionsSeek,
            new SeekSubscriptionRequest(StableId, Subscription, Generation, SubscriptionStart.FromBeginning), CommandKey),
        Case(McpCatalogExpectations.SubscriptionsReceive, new ReceiveSubscriptionRequest(StableId, Subscription), ReceiveKey),
        Case(McpCatalogExpectations.SubscriptionsComplete,
            new SubscriptionDeliveryCommand(StableId, Subscription, Token, DeliveryAction.Ack), CommandKey),
        Case(McpCatalogExpectations.SubscriptionsProcess,
            new SubscriptionProcessingRequest(StableId, Subscription, Token, Handler, Generation, Effects()), CommandKey),
        Case(McpCatalogExpectations.SubscriptionsPause, new SetSubscriptionPausedRequest(StableId, Subscription, Generation, true), CommandKey),
        Case(McpCatalogExpectations.ProjectionsConfigure, new ConfigureProjectionConsumerRequest(StableId, Consumer,
            new ProjectionConsumerDefinition(Generation, [Resource], [])), CommandKey),
        Case(McpCatalogExpectations.ProjectionsCommit, new CommitProjectionBatchRequest(StableId, Consumer, Token, Effects()), CommandKey),
        Case(McpCatalogExpectations.ProjectionsRelease, new ReleaseProjectionConsumerRequest(StableId, Consumer, Generation), CommandKey),
        Case(McpCatalogExpectations.OutboxPurge, new PurgeOutboxRequest(StableId, Partition, Position), CommandKey)
    ];

    internal static ImmutableArray<McpDecodeCase> Reads() =>
    [
        Read(McpCatalogExpectations.DocumentsGet, new GetDocumentRequest(Reference)),
        Read(McpCatalogExpectations.StreamsRead, new ReadStreamRequest(new StreamRef(Partition, Resource, Entity))),
        Read(McpCatalogExpectations.EventsRead, new ReadEventSourceRequest(Source)),
        Read(McpCatalogExpectations.SubscriptionsStatus, new GetSubscriptionRequest(Subscription)),
        Read(McpCatalogExpectations.MessagesInspect, new InspectMessageRequest(Lane, Entity)),
        Read(McpCatalogExpectations.GraphTraverse, new TraverseRequest(Partition, Resource, Reference)),
        Read(McpCatalogExpectations.SeriesRead, new ReadSamplesRequest(Partition, Resource, Entity, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)),
        Read(McpCatalogExpectations.QueryExecute, new QueryRequest(Partition, Sql)),
        Read(McpCatalogExpectations.QueryAst, Ast()),
        Read(McpCatalogExpectations.ChangesRead, new ReadChangeFeedRequest(Partition, Resource)),
        Read(McpCatalogExpectations.QueryLiveStart, new StartLiveQueryRequest(Ast())),
        Read(McpCatalogExpectations.QueryLiveRead, new ReadLiveQueryRequest(Ast(), Cursor)),
        Read(McpCatalogExpectations.OutboxStatus, new GetOutboxStatusRequest(Partition)),
        Read(McpCatalogExpectations.ProjectionsRead, new ReadProjectionBatchRequest(Consumer)),
        Read(McpCatalogExpectations.SearchExecute, new SearchRequest(Partition, Resource))
    ];

    private static ImmutableArray<Mutation> Effects() => [new PutDocument(Resource, Entity, EmptyJson)];

    private static McpDecodeCase Case<T>(string name, T request, string? idMember) =>
        new(name, JsonSerializer.SerializeToElement(request, JsonDefaults.Options), NativeSerialization.Serialize(request), StableId, idMember);

    private static McpDecodeCase Read<T>(string name, T request) =>
        new(name, JsonSerializer.SerializeToElement(request, JsonDefaults.Options), NativeSerialization.Serialize(request), Guid.Empty, null);
}

/// <summary>Owned canonical input and its exact expected bytes; no service or protocol substitute.</summary>
internal sealed record McpDecodeCase(string Name, JsonElement Request, ReadOnlyMemory<byte> ExpectedPayload,
    Guid CommandId, string? IdMember)
{
    internal IDictionary<string, JsonElement> Arguments()
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = Request
        };
        if (CommandId != Guid.Empty && IdMember is null)
        {
            arguments.Add(McpCanonicalTestData.CommandKey, JsonSerializer.SerializeToElement(CommandId, JsonDefaults.Options));
        }
        return arguments;
    }
}
