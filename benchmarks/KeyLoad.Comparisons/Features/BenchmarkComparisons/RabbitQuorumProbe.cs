using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class RabbitQuorumProbe
{
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
    private const int SingleNodeCount = 1;
    private const int ReplicatedNodeCount = 3;

    internal static async Task<(string Version, ClusterEvidence Evidence)?> TryReadReadyAsync(HttpClient management,
        string queue, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        var expected = topology == ComparisonTopology.Replicated ? ReplicatedNodeCount : SingleNodeCount;
        using var nodesJson = await ReadAsync(management, NodesPath, cancellationToken);
        var runningNodes = nodesJson.RootElement.EnumerateArray()
            .Where(node => node.GetProperty(RunningField).GetBoolean()).ToArray();
        var nodes = runningNodes.Select(node => node.GetProperty(NameField).GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
        using var overview = await ReadAsync(management, OverviewPath, cancellationToken);
        var version = overview.RootElement.GetProperty(VersionField).GetString() ?? string.Empty;
        using var queueJson = await ReadAsync(management, QueuesPath + Uri.EscapeDataString(queue), cancellationToken);
        var detail = queueJson.RootElement;
        var members = ReadNames(detail, MembersField);
        var online = ReadNames(detail, OnlineField);
        if (!IsReady(runningNodes, nodes, members, online, detail, version, expected))
        {
            return null;
        }

        var state = topology == ComparisonTopology.Replicated
            ? "healthy native quorum cluster"
            : "single native quorum node";
        var evidence = new ClusterEvidence(expected, expected, state,
            [$"connected running disc nodes={nodes.Length}", $"quorum members online={online.Length}", $"queue type={QuorumType}"]);
        return (version, evidence);
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute), token);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private static bool IsReady(JsonElement[] runningNodes, string[] nodes, string[] members, string[] online,
        JsonElement detail, string version, int expected)
        => nodes.Length == expected && nodes.Distinct(StringComparer.Ordinal).Count() == expected &&
           members.Length == expected && members.SequenceEqual(nodes, StringComparer.Ordinal) &&
           online.SequenceEqual(members, StringComparer.Ordinal) && detail.GetProperty(TypeField).GetString() == QuorumType &&
           runningNodes.All(node => node.GetProperty(NodeTypeField).GetString() == DiskNodeType) && version.Length != 0;

    private static string[] ReadNames(JsonElement detail, string field)
        => detail.GetProperty(field).EnumerateArray().Select(item => item.GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
}
