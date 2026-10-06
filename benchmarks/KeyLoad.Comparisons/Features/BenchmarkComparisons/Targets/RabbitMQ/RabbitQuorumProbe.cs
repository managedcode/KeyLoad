using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class RabbitQuorumProbe
{
    private const string ReadReadyConnectedRunningDiscNodesText = "connected running disc nodes=";
    private const string ReadReadyQuorumMembersOnlineText = "quorum members online=";
    private const string ReadReadyQueueTypeText = "queue type=";
    private const string ReadReadyRunningNodesText = "running nodes=";
    private const string ReadReadyQueueMembersText = "queue members=";
    private const string ReadReadyOnlineMembersText = "online members=";
    private const string ReadReadyQuorumText = "quorum=";
    private const string ReadReadyOfText = " of ";

    private const int NoItems = 0;

    private const string NodesPath = "/api/nodes";
    private const string OverviewPath = "/api/overview";
    private const string QueuesPath = "/api/queues/%2F/";
    private const string RunningField = "running";
    private const string NodeTypeField = "type";
    private const string DiskNodeType = "disc";
    private const string NameField = "name";
    private const string MembersField = "members";
    private const string OnlineField = "online";
    private const string TypeField = "type";
    private const string QuorumType = "quorum";
    private const string VersionField = "rabbitmq_version";
    private const string DurableField = "durable";

    internal static async Task<(string Version, ClusterEvidence Evidence)?> TryReadReadyAsync(HttpClient management,
        string queue, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        using var nodesJson = await ReadAsync(management, NodesPath, cancellationToken);
        using var overview = await ReadAsync(management, OverviewPath, cancellationToken);
        using var queueJson = await ReadAsync(management, QueuesPath + Uri.EscapeDataString(queue), cancellationToken);
        return ReadReady(nodesJson.RootElement, overview.RootElement, queueJson.RootElement, queue, topology);
    }

    internal static (string Version, ClusterEvidence Evidence)? ReadReady(JsonElement nodesJson, JsonElement overview,
        JsonElement detail, string queue, ComparisonTopology topology)
    {
        const int SingleItemCount = 1;
        const string HealthyNativeQuorumClusterToken = "healthy native quorum cluster";
        const string SingleNativeQuorumNodeToken = "single native quorum node";
        const char MemberSeparator = ',';
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;

        var expected = ComparisonTopologies.NodeCount(topology);
        var nativeNodes = nodesJson.EnumerateArray().ToArray();
        var nodes = nativeNodes.Select(node => node.GetProperty(NameField).GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
        var version = overview.GetProperty(VersionField).GetString() ?? string.Empty;
        var members = ReadNames(detail, MembersField);
        var online = ReadNames(detail, OnlineField);
        if (!IsReady(nativeNodes, nodes, members, online, detail, version, expected) ||
            detail.GetProperty(NameField).GetString() != queue)
        {
            return null;
        }

        var state = expected > SingleItemCount
            ? HealthyNativeQuorumClusterToken
            : SingleNativeQuorumNodeToken;
        var evidence = new ClusterEvidence(expected, expected, state,
            [$"{ReadReadyConnectedRunningDiscNodesText}{nodes.Length}", $"{ReadReadyQuorumMembersOnlineText}{online.Length}", $"{ReadReadyQueueTypeText}{QuorumType}",
                $"{ReadReadyRunningNodesText}{string.Join(MemberSeparator, nodes)}", $"{ReadReadyQueueMembersText}{string.Join(MemberSeparator, members)}",
                $"{ReadReadyOnlineMembersText}{string.Join(MemberSeparator, online)}", $"{ReadReadyQuorumText}{expected / MajorityDivisor + MajorityVoteOffset}{ReadReadyOfText}{expected}"]);
        return (version, evidence);
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute), token);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private static bool IsReady(JsonElement[] nativeNodes, string[] nodes, string[] members, string[] online,
        JsonElement detail, string version, int expected)
        => nodes.Length == expected && nodes.All(node => node.Length != NoItems) &&
           nodes.Distinct(StringComparer.Ordinal).Count() == expected &&
           members.Length == expected && members.SequenceEqual(nodes, StringComparer.Ordinal) &&
           online.SequenceEqual(members, StringComparer.Ordinal) && detail.GetProperty(TypeField).GetString() == QuorumType &&
           detail.GetProperty(DurableField).GetBoolean() &&
           nativeNodes.All(node => node.GetProperty(RunningField).GetBoolean() &&
               node.GetProperty(NodeTypeField).GetString() == DiskNodeType) && version.Length != NoItems;

    private static string[] ReadNames(JsonElement detail, string field)
        => detail.GetProperty(field).EnumerateArray().Select(item => item.GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
}
