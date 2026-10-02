using System.Net;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisNativeProtocol
{
    public const string ServerSection = "server";
    public const string ReplicationSection = "replication";
    public const string VersionField = "redis_version";
    public const string RunIdField = "run_id";
    public const string RoleField = "role";
    public const string MasterHostField = "master_host";
    public const string MasterPortField = "master_port";
    public const string LinkField = "master_link_status";
    public const string ReplicaCountField = "connected_slaves";
    public const string RoleCommand = "ROLE";
    public const string MasterRole = "master";
    public const string ReplicaRole = "slave";
    public const string LinkUp = "up";
    public const int RoleReplicaResponseLength = 5;
    public const int RoleNameIndex = 0;
    public const int RoleHostIndex = 1;
    public const int RolePortIndex = 2;
    public const string PrimaryEndpointError = "RedisPrimaryConfiguredEndpointInvalid";
    private const string EndpointSeparator = ":";

    public static EndPoint RequirePrimaryEndpoint(ConnectionMultiplexer connection)
        => ConfiguredEndpoint(connection, PrimaryEndpointError);

    public static EndPoint ConfiguredEndpoint(ConnectionMultiplexer connection, string error)
    {
        var endpoints = connection.GetEndPoints(configuredOnly: true);
        if (endpoints.Length != 1)
        {
            throw new ComparisonFailureException(error);
        }

        return endpoints[0];
    }

    public static bool SameEndpoint(EndPoint left, EndPoint right)
        => StringComparer.OrdinalIgnoreCase.Equals(EndpointIdentity(left), EndpointIdentity(right));

    public static bool EndpointMatches(EndPoint endpoint, string host, int port)
        => Port(endpoint) == port && StringComparer.OrdinalIgnoreCase.Equals(Host(endpoint), host);

    public static string EndpointIdentity(EndPoint endpoint) => Host(endpoint) + EndpointSeparator + Port(endpoint);

    private static string Host(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => dns.Host,
        IPEndPoint ip => ip.Address.ToString(),
        _ => string.Empty
    };

    private static int Port(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => dns.Port,
        IPEndPoint ip => ip.Port,
        _ => -1
    };

    public static async Task<Dictionary<string, string>> ReadInfoAsync(IServer server, string section,
        CommandFlags flags, CancellationToken token)
    {
        var result = await server.InfoRawAsync(section, flags).WaitAsync(token);
        return ParseInfo(result ?? string.Empty);
    }

    public static async Task<RedisResult[]> ReadRoleAsync(IServer server, CommandFlags flags, CancellationToken token)
    {
        var result = await server.ExecuteAsync(RoleCommand, Array.Empty<object>(), flags).WaitAsync(token);
        return (RedisResult[])result!;
    }

    public static int ParseInteger(Dictionary<string, string> info, string key)
        => int.TryParse(info.GetValueOrDefault(key), out var value) ? value : -1;

    private static Dictionary<string, string> ParseInfo(string info)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in info.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var pair = line.Split(':', 2);
            if (pair.Length == 2)
            {
                fields[pair[0].Trim()] = pair[1].Trim();
            }
        }
        return fields;
    }
}
