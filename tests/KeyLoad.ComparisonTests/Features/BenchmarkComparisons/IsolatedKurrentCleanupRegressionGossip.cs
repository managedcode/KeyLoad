using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentCleanupRegressionGossip
{
    private const string NodePrefix = "isolated-kurrent-", NativeSuffix = ".dev.internal", Http = "http";
    private const string InvalidGossip = "KurrentCleanupNativeGossipMismatch";
    private const int NativePort = 2113, MaximumBytes = 65_536;

    internal static async Task<Uri> ReadLeaderAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        var topology = ComparisonTopologies.FromNodeCount(nodeCount);
        var names = Enumerable.Range(1, nodeCount).Select(index => NodePrefix + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        var views = new KurrentGossipView[nodeCount];
        var endpoints = new Uri[nodeCount];
        for (var index = 0; index < nodeCount; index++)
        {
            endpoints[index] = app.GetEndpoint(names[index], Http);
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler, disposeHandler: false)
            { BaseAddress = endpoints[index], Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(new Uri(KurrentConstants.GossipPath, UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var json = await ReadBoundedAsync(response.Content, token);
            views[index] = ParseView(json.RootElement, names[index] + NativeSuffix);
        }
        if (!KurrentClusterMembers.IsReady(views, topology) || views.Any(view => view.Members.Any(member =>
            member.HttpEndpointPort != NativePort || !names.Any(name => member.HttpEndpointIp == name + NativeSuffix))))
        {
            throw new ComparisonFailureException(InvalidGossip);
        }
        var leader = Array.FindIndex(views, view => view.LocalMember.State == KurrentConstants.LeaderState);
        return leader >= 0 ? endpoints[leader] : throw new ComparisonFailureException(InvalidGossip);
    }

    private static KurrentGossipView ParseView(JsonElement root, string nativeHost)
    {
        var serverIp = Property(root, KurrentConstants.ServerIpField).GetString();
        var serverPort = Property(root, KurrentConstants.ServerPortField).GetInt32();
        var members = Property(root, KurrentConstants.MembersField).EnumerateArray().Select(ParseMember).ToArray();
        var local = members.Where(member => member.HttpEndpointIp == nativeHost && member.HttpEndpointPort == NativePort).ToArray();
        if (serverIp != nativeHost || serverPort != NativePort || local.Length != 1)
        {
            throw new ComparisonFailureException(InvalidGossip);
        }
        // These are original native identities; the mapped transport endpoint is kept separately.
        return new(serverIp, serverPort, members, local[0]);
    }

    private static KurrentGossipMember ParseMember(JsonElement member)
        => new(Text(member, KurrentConstants.InstanceIdField), Text(member, KurrentConstants.StateField),
            Text(member, KurrentConstants.VersionField), Text(member, KurrentConstants.HttpEndpointIpField),
            Property(member, KurrentConstants.HttpEndpointPortField).GetInt32(), Text(member, KurrentConstants.InternalHttpEndpointIpField),
            Property(member, KurrentConstants.InternalHttpEndpointPortField).GetInt32(), Property(member, KurrentConstants.AliveField).GetBoolean(),
            Property(member, KurrentConstants.ReadOnlyField).GetBoolean(), Checkpoint(member, KurrentConstants.LastCommitPositionField),
            Checkpoint(member, KurrentConstants.WriterCheckpointField), Checkpoint(member, KurrentConstants.ChaserCheckpointField));

    private static string Text(JsonElement element, string name)
        => Property(element, name).GetString() ?? throw new ComparisonFailureException(InvalidGossip);

    private static long Checkpoint(JsonElement member, string name)
    {
        var value = Property(member, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out number) ? number : throw new ComparisonFailureException(InvalidGossip);
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count(property => property.NameEquals(name)) != 1)
        {
            throw new ComparisonFailureException(InvalidGossip);
        }
        return element.GetProperty(name);
    }

    private static async Task<JsonDocument> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        var bytes = new byte[MaximumBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), token);
            if (read == 0)
            {
                return JsonDocument.Parse(bytes.AsMemory(0, length));
            }
            length += read;
            if (length > MaximumBytes)
            {
                throw new ComparisonFailureException(InvalidGossip);
            }
        }
        throw new ComparisonFailureException(InvalidGossip);
    }
}
