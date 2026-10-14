using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPureReadRf3Protocol
{
    internal const string Missing = "native-read-absent";
    internal const string Marker = "native-pure-read-";
    internal const long EpochStep = 1;
    internal const string GraphSql = "EXEC " + McpToolNames.GraphTraverse + " $1";
    internal const string DocumentSql = "EXEC " + McpToolNames.DocumentsGet + " $1";
    internal const string MessageSql = "EXEC " + McpToolNames.MessagesInspect + " $1";
    internal const string TargetId = PostgresCompositionRf3Protocol.TargetPrefix
        + PostgresCompositionRf3Protocol.EdgePrefix + PostgresCompositionRf3Protocol.Message;

    internal static NpgsqlParameter[] Parameters<T>(T request)
    {
        var envelope = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        };
        return [new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(envelope, JsonDefaults.Options) }];
    }

    internal static TraverseRequest Graph() => new(PostgresRf3State.Partition,
        PostgresCompositionRf3Protocol.Graph, PostgresRf3State.Left);
    internal static InspectMessageRequest Message(string queue, string id)
        => new(new(PostgresRf3State.Partition, queue), id);
}
