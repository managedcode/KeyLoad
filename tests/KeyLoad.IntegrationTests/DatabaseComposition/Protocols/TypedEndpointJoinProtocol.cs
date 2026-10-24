using System.Text.Json;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class TypedEndpointJoinProtocol
{
    private const string GraphFieldKey = "graph";
    private const string PrefixFieldKey = "prefix";
    private const string QueueFieldKey = "queue";
    private const string FromFieldKey = "from";
    private const string ToFieldKey = "to";
    private const string LimitFieldKey = "limit";
    private const string CommandFieldKey = "command";
    internal const string FromCollection = "join-from";
    internal const string ToCollection = "join-to";
    internal const string Queue = "join-actions";
    internal const string EmptyQueue = "join-empty";
    internal const string EmptyPrefix = "empty-";
    internal const string MalformedMessage = "wrong-endpoint";
    internal const int NoEffects = 0;
    internal const string Graph = "join-knowledge";
    internal const string First = "a";
    internal const string Second = "b";
    internal const string Hidden = "h";
    internal const string Missing = "m";
    internal const string MatchedMessage = "a-match";
    internal const string MissingMessage = "b-missing";
    internal const string HiddenMessage = "c-hidden";
    internal const string Prefix = "joined-";
    internal const string Label = "derived-link";
    internal const string Attributes = "{}";
    internal const string FirstJson = "{\"id\":\"a\",\"title\":\"first\"}";
    internal const string SecondJson = "{\"id\":\"b\",\"title\":\"second\"}";
    internal const string HiddenJson = "{\"id\":\"h\",\"title\":\"hidden\"}";
    internal const string UseGrant = "join.pk.use";
    internal const string BlockedUseGrant = "join.pk.blocked";
    internal const int SourceCount = 3;
    internal const long FirstRevision = 1;
    internal const int Dialect = SqlOperationProtocol.DefaultQueryDialectVersion;
    internal const int ColdCuts = 2;
    internal static string[] Messages => [MatchedMessage, MissingMessage, HiddenMessage];

    internal static QueueToGraph Mutation(string prefix = Prefix, int maximum = SourceCount)
        => new(Graph, Queue, prefix, maximum, FromCollection, ToCollection);

    internal static SqlOperationRequest Sql(CommandRequest command)
    {
        var mutation = (QueueToGraph)command.Mutations[0];
        const string text = "INSERT INTO GRAPH_EDGES (command_id, graph, edge_prefix, max_messages) "
            + "SELECT @command, @graph, @prefix, @limit FROM QUEUE_MESSAGES(@queue) AS q "
            + "INNER JOIN DOCUMENTS(@from) AS f ON q.from_entity_ref = f.entity_ref "
            + "INNER JOIN DOCUMENTS(@to) AS t ON q.to_entity_ref = t.entity_ref";
        return new(command.Partition, text, new(StringComparer.Ordinal)
        {
            [GraphFieldKey] = JsonSerializer.SerializeToElement(mutation.Graph, JsonDefaults.Options),
            [PrefixFieldKey] = JsonSerializer.SerializeToElement(mutation.EdgeIdPrefix, JsonDefaults.Options),
            [QueueFieldKey] = JsonSerializer.SerializeToElement(mutation.Queue, JsonDefaults.Options),
            [FromFieldKey] = JsonSerializer.SerializeToElement(mutation.FromCollection, JsonDefaults.Options),
            [ToFieldKey] = JsonSerializer.SerializeToElement(mutation.ToCollection, JsonDefaults.Options),
            [LimitFieldKey] = JsonSerializer.SerializeToElement(mutation.MaxMessages, JsonDefaults.Options),
            [CommandFieldKey] = JsonSerializer.SerializeToElement(command.CommandId, JsonDefaults.Options)
        }, QueryDialectVersion: Dialect);
    }

    internal static EntityRef From(PartitionRef partition) => new(partition, FromCollection, First);
    internal static EntityRef To(PartitionRef partition) => new(partition, ToCollection, Second);
}
