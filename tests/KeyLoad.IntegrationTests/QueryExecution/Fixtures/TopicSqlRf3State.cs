using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record TopicSqlRf3State(PartitionRef Partition, CommandRequest Command, CommitReceipt Receipt,
    EventSourcePage Original, MessageInspection OriginalQueue, McpPersistedIdentity Identity)
{
    internal EventSourceRef Source => new(Partition, TopicSqlRf3Protocol.Topic, EventSourceKind.Topic);
    internal ReadEventSourceRequest Read => new(Source);
    internal InspectMessageRequest Inspect => new(new(Partition, TopicSqlRf3Protocol.Queue), TopicSqlRf3Protocol.Message);
    internal QueryRequest Sql(string sql = TopicSqlRf3Protocol.Sql) => new(Partition, sql, AllowFullScan: true);
    internal AstQueryRequest Ast => new(Partition,
        new(TopicSqlRf3Protocol.Topic, null, [new(TopicSqlRf3Protocol.Star, TopicSqlRf3Protocol.Star)], null,
            [new(TopicSqlRf3Protocol.PositionPath, false)], TopicSqlRf3Protocol.PageLimit,
            ModelSource: new(ModelQuerySourceKind.TopicEvents, TopicSqlRf3Protocol.Topic)), AllowFullScan: true);
    internal static ImmutableArray<EventData> Data =>
        [new(TopicSqlRf3Protocol.First, TopicSqlRf3Protocol.Created, TopicSqlRf3Protocol.Payload, TopicSqlRf3Protocol.Headers),
         new(TopicSqlRf3Protocol.Second, TopicSqlRf3Protocol.Updated, TopicSqlRf3Protocol.NextPayload, TopicSqlRf3Protocol.Headers)];
}
