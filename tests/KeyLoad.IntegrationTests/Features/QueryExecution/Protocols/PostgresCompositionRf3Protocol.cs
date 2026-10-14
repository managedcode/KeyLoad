using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresCompositionRf3Protocol
{
    internal const string SourceQueue = "native-composition-source";
    internal const string TargetQueue = "native-composition-target";
    internal const string Graph = "native-composition-graph";
    internal const string Message = "source-link";
    internal const string EdgePrefix = "native-edge-";
    internal const string TargetPrefix = "native-message-";
    internal const string Label = "native-derived";
    internal const string Attributes = "{}";
    internal const int One = 1;
    internal const long FirstRevision = 1;
    private const string GuidFormat = "D";
    private const string Forward = "INSERT INTO GRAPH_EDGES (command_id, graph, edge_prefix, max_messages) "
        + "SELECT $1, $2, $3, $4 FROM QUEUE_MESSAGES($5)";
    private const string JoinedForward = Forward + " AS q INNER JOIN DOCUMENTS($6) AS f "
        + "ON q.from_entity_ref = f.entity_ref INNER JOIN DOCUMENTS($7) AS t ON q.to_entity_ref = t.entity_ref";
    private const string Reverse = "INSERT INTO QUEUE_MESSAGES (command_id, queue, message_prefix) "
        + "SELECT $1, $2, $3 FROM GRAPH_TRAVERSE($4, $5, $6, $7, $8)";

    internal static string Sql(CommandRequest command)
        => command.Mutations[0] switch
        {
            QueueToGraph { FromCollection: not null } => JoinedForward,
            QueueToGraph => Forward,
            GraphToQueueMutation => Reverse,
            _ => throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult)
        };

    internal static NpgsqlParameter[] Parameters(CommandRequest command)
    {
        var id = Text(command.CommandId.ToString(GuidFormat));
        if (command.Mutations[0] is QueueToGraph forward)
        {
            NpgsqlParameter[] values = [id, Text(forward.Graph), Text(forward.EdgeIdPrefix),
                Integer(forward.MaxMessages), Text(forward.Queue)];
            return forward.FromCollection is null ? values
                : [.. values, Text(forward.FromCollection), Text(forward.ToCollection!)];
        }
        var reverse = (GraphToQueueMutation)command.Mutations[0];
        return [id, Text(reverse.Queue), Text(reverse.MessageIdPrefix), Text(reverse.Graph),
            new() { NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = global::System.Text.Json.JsonSerializer.Serialize(reverse.Start, JsonDefaults.Options) },
            Integer(reverse.MaxDepth), Integer(reverse.MaxVertices), Integer(reverse.MaxEdges)];
    }

    private static NpgsqlParameter Text(string value) => new() { NpgsqlDbType = NpgsqlDbType.Text, Value = value };
    private static NpgsqlParameter Integer(int value) => new() { NpgsqlDbType = NpgsqlDbType.Integer, Value = value };
}
