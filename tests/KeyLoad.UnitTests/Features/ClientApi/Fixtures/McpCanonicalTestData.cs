using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Actual public contract instances for public JSON, native payload and stable-identity assertions.</summary>
internal static class McpCanonicalTestData
{
    internal const string RequestKey = "request";
    internal const string CommandKey = "commandId";
    internal const string ReceiveKey = "requestId";
    internal const string UnknownKey = "principalId";
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
    internal const string FieldPath = "/" + Field;
    internal const string Cursor = "bounded-cursor";
    private const string Token = "delivery-token";
    private const string Handler = "handler-scope";
    private const string ApiKey = "key-record";
    private const string Verifier = "persisted-verifier";
    private const string Sql = "SELECT * FROM mcp-records";
    private const string GraphPathSql = "SELECT * FROM GRAPH_SHORTEST_PATH('mcp-records', 'mcp-records', 'mcp-entity', 'mcp-records', 'mcp-entity', 16, 1000, 5000);";
    private const int ContractVersion = 1;
    private const string PlacementShardIdText = "79214e37-a89c-431f-b4e2-202d5d967883";
    private const long Generation = 1;
    private const long WaitMinimumPosition = 1;
    private const long WaitPlacementEpoch = 1;
    private const long Position = 0;
    private const int Limit = 10;
    internal static readonly Guid StableId = Guid.Parse(StableIdText);
    private static readonly Guid PlacementShardId = Guid.Parse(PlacementShardIdText);
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static readonly EntityRef Reference = new(Partition, Resource, Entity);
    internal static readonly QueueLaneRef Lane = new(Partition, Resource);
    internal static readonly EventSourceRef Source = new(Partition, Resource, EventSourceKind.Topic);
    internal static readonly SubscriptionRef Subscription = new(Source, Entity);
    internal static readonly ProjectionConsumerRef Consumer = new(Partition, Resource);

    internal static AstQueryRequest Ast() => new(Partition,
        new SelectQuery(Resource, null, [new Selection(FieldPath, Field)], null, [], Limit));

    internal static ImmutableArray<McpDecodeCase> Commands() =>
    [
        Case(McpCatalogExpectations.DocumentsCommit, new CommandRequest(StableId, Partition, Effects()), CommandKey),
        Case("keyload_messages_receive_across_lanes", new MultiLaneReceiveRequest(StableId,
            [new ReceiveRequest(Guid.Parse("00000000-0000-0000-0000-000000000002"), Lane)]), ReceiveKey),
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
        Case(McpCatalogExpectations.OutboxPurge, new PurgeOutboxRequest(StableId, Partition, Position), CommandKey),
        Case(McpCatalogExpectations.AdminPartitionPlacementBind,
            new BindAtomicPartitionPlacementRequest(ContractVersion, 0, Partition, PlacementShardId), null)
    ];

    internal static ImmutableArray<McpDecodeCase> Reads() =>
    [
        Read(McpCatalogExpectations.DocumentsGet, new GetDocumentRequest(Reference)),
        Read(McpCatalogExpectations.DocumentsReadFollower, new ReadFollowerDocumentRequestV1(ContractVersion, Reference,
            "http://node2:8080", WaitMinimumPosition, new CommitToken(StableId, Partition.AtomicPartitionId,
                WaitMinimumPosition, WaitPlacementEpoch))),
        Read(McpCatalogExpectations.StreamsRead, new ReadStreamRequest(new StreamRef(Partition, Resource, Entity))),
        Read(McpCatalogExpectations.StreamsReplay,
            new ReadAggregateReplayRequest(new StreamRef(Partition, Resource, Entity), "worker.v1")),
        Read(McpCatalogExpectations.EventsRead, new ReadEventSourceRequest(Source)),
        Read(McpCatalogExpectations.SubscriptionsStatus, new GetSubscriptionRequest(Subscription)),
        Read(McpCatalogExpectations.MessagesInspect, new InspectMessageRequest(Lane, Entity)),
        Read(McpCatalogExpectations.QueueTransferInspect, new InspectQueueTransferRequest(Lane, StableId)),
        Read(McpCatalogExpectations.QueueTransferReceipt, new InspectQueueTransferReceiptRequest(Lane, Lane, StableId)),
        Read(McpCatalogExpectations.ScheduleInspect, new InspectRecurringScheduleRequest(Lane, StableId)),
        Read(McpCatalogExpectations.SagaInspect, new InspectSagaRequest(Lane, StableId)),
        Read(McpCatalogExpectations.GraphTraverse, new TraverseRequest(Partition, Resource, Reference)),
        Read(McpCatalogExpectations.GraphShortestPath,
            new GraphShortestPathRequest(ContractVersion, Partition, Resource, Reference, Reference)),
        Read(McpCatalogExpectations.GraphIncomingEdges, GraphIncomingMcpTestData.Request()),
        Read(McpCatalogExpectations.QueryGraphPath,
            new SqlGraphPathRequest(ContractVersion, new QueryRequest(Partition, GraphPathSql, AllowFullScan: true))),
        Read(McpCatalogExpectations.AdminPartitionPlacementRead,
            new AtomicPartitionPlacementReadRequest(ContractVersion, Partition)),
        Read(McpCatalogExpectations.SeriesRead, new ReadSamplesRequest(Partition, Resource, Entity, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)),
        Read(McpCatalogExpectations.QueryExecute, new QueryRequest(Partition, Sql)),
        Read(McpCatalogExpectations.QuerySearch, new SqlGraphSearchRequest(1, new(Partition, Sql))),
        Read(McpCatalogExpectations.QueryPartitions, PartitionQuery()),
        Read(McpCatalogExpectations.QueryDistributedSearch,
            new DistributedSearchRequestV1(ContractVersion, [Partition], new SearchRequest(Partition, Resource))),
        Read(McpCatalogExpectations.QueryAst, Ast()),
        Read(McpCatalogExpectations.ChangesRead, new ReadChangeFeedRequest(Partition, Resource)),
        Read(McpCatalogExpectations.QueryLiveStart, new StartLiveQueryRequest(Ast())),
        Read(McpCatalogExpectations.QueryLiveRead, new ReadLiveQueryRequest(Ast(), Cursor)),
        Read(McpCatalogExpectations.OutboxStatus, new GetOutboxStatusRequest(Partition)),
        Read(McpCatalogExpectations.ProjectionsRead, new ReadProjectionBatchRequest(Consumer)),
        Read(WaitForIndexProtocol.Tool, new WaitForIndexRequest(Partition, Resource, FieldPath, new CommitToken(StableId, Partition.AtomicPartitionId, WaitMinimumPosition, WaitPlacementEpoch))),
        Read(McpCatalogExpectations.SearchExecute, new SearchRequest(Partition, Resource)),
        Read(McpCatalogExpectations.SearchGraph, new GraphSearchRequest(1, new(Partition, Resource),
            Retriever: new(new(Resource, [Reference])))),
        Read(McpCatalogExpectations.SeriesRollup, new ReadSampleRollupRequest(Partition, Resource, Entity, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1))),
        Read(McpCatalogExpectations.SeriesRetention, new ReadSampleRetentionRequest(Partition, Resource, Entity))
    ];

    private static PartitionQueryRequestV1 PartitionQuery() => new(1, [Partition],
        new SelectQuery(Resource, null, [new Selection(FieldPath, Field)], null, [], Limit),
        null, false, 1);

    private static ImmutableArray<Mutation> Effects() => [new PutDocument(Resource, Entity, EmptyJson)];

    private static McpDecodeCase Case<T>(string name, T request, string? idMember) =>
        CreateCase(name, request, StableId, idMember);

    private static McpDecodeCase Read<T>(string name, T request) =>
        CreateCase(name, request, Guid.Empty, null);

    private static McpDecodeCase CreateCase<T>(string name, T request, Guid commandId, string? idMember) =>
        new(name, JsonSerializer.SerializeToElement(request, JsonDefaults.Options), commandId, idMember,
            element => element.Deserialize<T>(JsonDefaults.Options)!,
            payload => NativeSerialization.Deserialize<T>(payload.Span)!,
            value => NativeSerialization.Serialize((T)value),
            (value, destination) => NativeSerialization.Serialize((T)value, destination),
            value => JsonSerializer.SerializeToElement((T)value, JsonDefaults.Options));
}

/// <summary>Owned public JSON and its independent actual-type decoding operations.</summary>
internal sealed record McpDecodeCase(string Name, JsonElement Request, Guid CommandId, string? IdMember,
    Func<JsonElement, object> DeserializePublic, Func<ReadOnlyMemory<byte>, object> DeserializeNative,
    Func<object, byte[]> SerializeNative, Action<object, Stream> SerializeNativeToStream,
    Func<object, JsonElement> SerializePublic)
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
