using System.Net;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisNativeProtocol
{
    private const int MissingItemIndex = -1;

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
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        var endpoints = connection.GetEndPoints(configuredOnly: true);
        if (endpoints.Length != SingleItemCount)
        {
            throw new ComparisonFailureException(error);
        }

        return endpoints[FirstElementIndex];
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
        _ => MissingItemIndex
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
        => int.TryParse(info.GetValueOrDefault(key), out var value) ? value : MissingItemIndex;

    private static Dictionary<string, string> ParseInfo(string info)
    {
        const char LineFeed = '\n';
        const char InfoCommentMarker = '#';
        const char FieldSeparator = ':';
        const int InfoFieldPairWidth = 2;
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in info.Split(LineFeed, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(InfoCommentMarker))
            {
                continue;
            }

            var pair = line.Split(FieldSeparator, InfoFieldPairWidth);
            if (pair.Length == InfoFieldPairWidth)
            {
                fields[pair[FirstElementIndex].Trim()] = pair[SingleItemCount].Trim();
            }
        }
        return fields;
    }
}
