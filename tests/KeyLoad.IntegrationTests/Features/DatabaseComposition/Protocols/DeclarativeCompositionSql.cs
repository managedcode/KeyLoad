using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeCompositionSql
{
    private const string ForwardText = "INSERT INTO GRAPH_EDGES(@graph, @prefix) SELECT LINKS FROM QUEUE_MESSAGES(@queue) LIMIT @limit WITH COMMAND @command";
    private const string ReverseText = "INSERT INTO QUEUE_MESSAGES(@queue, @prefix) SELECT LINKS FROM GRAPH_TRAVERSE(@graph, @start, @depth, @vertices, @edges) WITH COMMAND @command";

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
