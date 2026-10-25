using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class DeclarativeCompositionSql
{
    private const string ForwardText = "INSERT INTO GRAPH_EDGES (command_id, graph, edge_prefix, max_messages) SELECT @command, @graph, @prefix, @limit FROM QUEUE_MESSAGES(@queue)";
    private const string ReverseText = "INSERT INTO QUEUE_MESSAGES (command_id, queue, message_prefix) SELECT @command, @queue, @prefix FROM GRAPH_TRAVERSE(@graph, @start, @depth, @vertices, @edges)";

    internal static SqlOperationRequest Forward(CommandRequest command, QueueToGraph mutation)
        => new(command.Partition, ForwardText, Values(
            ("graph", mutation.Graph), ("prefix", mutation.EdgeIdPrefix), ("queue", mutation.Queue),
            ("limit", mutation.MaxMessages), ("command", command.CommandId)));

    internal static SqlOperationRequest Reverse(CommandRequest command, GraphToQueueMutation mutation)
        => new(command.Partition, ReverseText, Values(
            ("queue", mutation.Queue), ("prefix", mutation.MessageIdPrefix), ("graph", mutation.Graph),
            ("start", mutation.Start), ("depth", mutation.MaxDepth), ("vertices", mutation.MaxVertices),
            ("edges", mutation.MaxEdges), ("command", command.CommandId)));

    private static Dictionary<string, JsonElement> Values(params (string Name, object Value)[] values)
        => values.ToDictionary(value => value.Name,
            value => JsonSerializer.SerializeToElement(value.Value, JsonDefaults.Options), StringComparer.Ordinal);
}
