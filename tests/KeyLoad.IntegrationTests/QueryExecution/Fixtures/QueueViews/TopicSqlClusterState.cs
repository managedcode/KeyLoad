using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record TopicSqlClusterState(PartitionRef Partition, CommandRequest Command, CommitReceipt Receipt,
    EventSourcePage Original, MessageInspection OriginalQueue, McpPersistedIdentity Identity)
{
    internal EventSourceRef Source => new(Partition, TopicSqlClusterProtocol.Topic, EventSourceKind.Topic);
    internal ReadEventSourceRequest Read => new(Source);
    internal InspectMessageRequest Inspect => new(new(Partition, TopicSqlClusterProtocol.Queue), TopicSqlClusterProtocol.Message);
    internal QueryRequest Sql(string sql = TopicSqlClusterProtocol.Sql) => new(Partition, sql, AllowFullScan: true);
    internal AstQueryRequest Ast => new(Partition,
        new(TopicSqlClusterProtocol.Topic, null, [new(TopicSqlClusterProtocol.Star, TopicSqlClusterProtocol.Star)], null,
            [new(TopicSqlClusterProtocol.PositionPath, false)], TopicSqlClusterProtocol.PageLimit,
            ModelSource: new(ModelQuerySourceKind.TopicEvents, TopicSqlClusterProtocol.Topic)), AllowFullScan: true);
    internal static ImmutableArray<EventData> Data =>
        [new(TopicSqlClusterProtocol.First, TopicSqlClusterProtocol.Created, TopicSqlClusterProtocol.Payload, TopicSqlClusterProtocol.Headers),
         new(TopicSqlClusterProtocol.Second, TopicSqlClusterProtocol.Updated, TopicSqlClusterProtocol.NextPayload, TopicSqlClusterProtocol.Headers)];
}
