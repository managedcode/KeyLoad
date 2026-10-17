using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClosedCommandRf3Protocol
{
    internal const string AdminMarker = "native-closed-admin-";
    internal const string TargetMarker = "native-closed-target-";
    internal const long EpochStep = 1;
    internal const string UnsupportedState = "0A000";
    private const string Binding = "args";
    private const string NativeSql = "EXEC " + McpToolNames.PrincipalsConfigure + " $1";
    private const string NamedSql = "EXEC " + McpToolNames.PrincipalsConfigure + " @" + Binding;

    internal static string Sql => NativeSql;
    internal static NpgsqlParameter[] Parameters(PostgresClosedCommandRf3State state)
        => [new() { NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(Envelope(state), JsonDefaults.Options) }];

    internal static SqlOperationRequest Operation(PostgresClosedCommandRf3State state)
        => new(PostgresRf3State.Partition, NamedSql,
            Parameters: new(StringComparer.Ordinal)
            {
                [Binding] = JsonSerializer.SerializeToElement(Envelope(state), JsonDefaults.Options)
            });

    private static Dictionary<string, JsonElement> Envelope(PostgresClosedCommandRf3State state)
        => new(StringComparer.Ordinal)
        {
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(
                new ConfigurePrincipalRequest(state.Denied), JsonDefaults.Options),
            [McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(state.CommandId, JsonDefaults.Options)
        };
}
