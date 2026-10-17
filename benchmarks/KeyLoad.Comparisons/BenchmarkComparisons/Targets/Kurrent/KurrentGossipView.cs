using System.Net;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal sealed record KurrentGossipMember(string Id, string State, string Version, string HttpEndpointIp,
    int HttpEndpointPort, string InternalHttpEndpointIp, int InternalHttpEndpointPort, bool IsAlive,
    bool IsReadOnly, long Commit, long Writer, long Chaser);

internal sealed record KurrentGossipView(string ServerIp, int ServerPort, KurrentGossipMember[] Members,
    KurrentGossipMember LocalMember)
{
    public static async Task<KurrentGossipView> ReadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        // Aspire must construct these borrowed clients with redirects disabled; their handlers cannot be retrofitted here.
        var endpoint = client.BaseAddress ?? throw new ComparisonFailureException(KurrentConstants.InvalidTopology);
        using var response = await client.GetAsync(new Uri(KurrentConstants.GossipPath, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(json.RootElement, endpoint);
    }

    private static KurrentGossipView Parse(JsonElement root, Uri endpoint)
    {
        const int FirstElementIndex = 0;

        var serverIp = root.GetProperty(KurrentConstants.ServerIpField).GetString() ?? KurrentConstants.Empty;
        var serverPort = root.GetProperty(KurrentConstants.ServerPortField).GetInt32();
        var members = ReadMembers(root.GetProperty(KurrentConstants.MembersField));
        ValidateWrapperEndpoint(endpoint, serverIp, serverPort);
        var local = members.Where(member => SameEndpoint(endpoint.Host, endpoint.Port, member.HttpEndpointIp,
            member.HttpEndpointPort)).ToArray();
        if (local.Length != KurrentConstants.ExpectedLocalEndpointMatch)
        {
            throw new ComparisonFailureException(KurrentConstants.EndpointAuthorityMismatch);
        }

        return new KurrentGossipView(serverIp, serverPort, members, local[FirstElementIndex]);
    }

    private static KurrentGossipMember[] ReadMembers(JsonElement members)
    {
        var result = new List<KurrentGossipMember>();
        foreach (var member in members.EnumerateArray())
        {
            result.Add(new KurrentGossipMember(
                ReadText(member, KurrentConstants.InstanceIdField),
                ReadText(member, KurrentConstants.StateField),
                ReadText(member, KurrentConstants.VersionField),
                ReadText(member, KurrentConstants.HttpEndpointIpField),
                member.GetProperty(KurrentConstants.HttpEndpointPortField).GetInt32(),
                ReadText(member, KurrentConstants.InternalHttpEndpointIpField),
                member.GetProperty(KurrentConstants.InternalHttpEndpointPortField).GetInt32(),
                member.GetProperty(KurrentConstants.AliveField).GetBoolean(),
                member.GetProperty(KurrentConstants.ReadOnlyField).GetBoolean(),
                ReadInt64(member, KurrentConstants.LastCommitPositionField),
                ReadInt64(member, KurrentConstants.WriterCheckpointField),
                ReadInt64(member, KurrentConstants.ChaserCheckpointField)));
        }

        return result.ToArray();
    }

    private static void ValidateWrapperEndpoint(Uri endpoint, string serverIp, int serverPort)
    {
        if (!SameEndpoint(endpoint.Host, endpoint.Port, serverIp, serverPort))
        {
            throw new ComparisonFailureException(KurrentConstants.EndpointAuthorityMismatch);
        }
    }

    private static bool SameEndpoint(string expectedHost, int expectedPort, string observedHost, int observedPort)
        => expectedPort == observedPort && SameHost(expectedHost, observedHost);

    private static bool SameHost(string expected, string observed)
    {
        if (IPAddress.TryParse(expected, out var expectedAddress) && IPAddress.TryParse(observed, out var observedAddress))
        {
            return expectedAddress.Equals(observedAddress);
        }

        return string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadText(JsonElement member, string property)
        => member.GetProperty(property).GetString() ?? KurrentConstants.Empty;

    private static long ReadInt64(JsonElement member, string property)
    {
        var value = member.GetProperty(property);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return KurrentConstants.UnknownCheckpointPosition;
    }
}
