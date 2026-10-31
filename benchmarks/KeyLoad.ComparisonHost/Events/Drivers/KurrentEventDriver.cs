using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>
/// KurrentDB through KurrentDB.Client over gRPC with leader node preference: <c>AppendToStreamAsync</c> with
/// <see cref="StreamState.NoStream"/> per append, one atomic <c>MultiStreamAppendAsync</c> of new streams per load batch,
/// and a forward <c>ReadStreamAsync</c> of one event from the stream start.
/// </summary>
internal sealed class KurrentEventDriver(BenchmarkConnection connection) : IEventDriver
{
    private const int Batch = 100;
    private const int ReadLimit = 1;
    private const int ExpectedLeaders = 1;
    private const string ContentType = "application/json";
    private const string GossipPath = "gossip?format=json";
    private const string MembersField = "members";
    private const string StateField = "state";
    private const string AliveField = "isAlive";
    private const string InstanceField = "instanceId";
    private const string LeaderState = "Leader";
    private const string FollowerState = "Follower";
    private const string ViewSeparator = ", ";
    private const string NotPrepared = "Prepare the KurrentDB driver before using it.";
    private const string NotAcknowledged = "KurrentDB did not acknowledge the append to ";
    private const string MissingStream = "KurrentDB stream not found: ";
    private const string NotReady = "KurrentDB nodes did not agree on one leader with live followers; leader views: ";
    private static readonly TimeSpan ReadinessPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);
    private KurrentDBClient? client;

    public int BatchSize => Batch;

    private KurrentDBClient Client => client ?? throw new InvalidOperationException(NotPrepared);

    /// <summary>Waits until every node sees the same leader, then creates the client that discovers and writes to it.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        await WaitForLeaderAsync(token);
        var settings = KurrentDBClientSettings.Create(connection.RequireConnectionString());
        settings.ConnectivitySettings.NodePreference = NodePreference.Leader;
        client = new KurrentDBClient(settings);
    }

    public async Task AppendManyAsync(IReadOnlyList<EventRecord> events, CancellationToken token)
    {
        AppendStreamRequest[] requests = [.. events.Select(record => new AppendStreamRequest(record.Stream, StreamState.NoStream, [Event(record)]))];
        _ = await Client.MultiStreamAppendAsync(requests, token);
    }

    public async Task AppendAsync(EventRecord record, CancellationToken token)
    {
        var result = await Client.AppendToStreamAsync(record.Stream, StreamState.NoStream, [Event(record)], cancellationToken: token);
        if (result is not SuccessResult)
        {
            throw new InvalidOperationException(NotAcknowledged + record.Stream);
        }
    }

    public async Task<EventRecord> ReadFirstAsync(string stream, CancellationToken token)
    {
        await using var read = Client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, maxCount: ReadLimit,
            cancellationToken: token);
        if (await read.ReadState == ReadState.StreamNotFound)
        {
            throw new KeyNotFoundException(MissingStream + stream);
        }
        await foreach (var resolved in read)
        {
            var found = resolved.Event;
            return new(stream, found.EventId.ToGuid(), found.EventType, Encoding.UTF8.GetString(found.Data.Span));
        }
        throw new KeyNotFoundException(MissingStream + stream);
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    private static KurrentEventData Event(EventRecord record)
        => new(Uuid.FromGuid(record.EventId), record.Type, Encoding.UTF8.GetBytes(record.Json), contentType: ContentType);

    private async Task WaitForLeaderAsync(CancellationToken token)
    {
        HttpClient[] nodes = [.. Enumerable.Range(0, connection.Endpoints.Count).Select(node => connection.CreateHttpClient(node))];
        try
        {
            await PollLeaderAsync(nodes, token);
        }
        finally
        {
            foreach (var node in nodes)
            {
                node.Dispose();
            }
        }
    }

    private async Task PollLeaderAsync(HttpClient[] nodes, CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        var state = string.Empty;
        while (TimeProvider.System.GetElapsedTime(started) < ReadinessLimit)
        {
            try
            {
                var leaders = await Task.WhenAll(nodes.Select(node => LeaderAsync(node, connection.Nodes, token)));
                if (Array.TrueForAll(leaders, leader => leader.Length > 0 && leader == leaders[0]))
                {
                    return;
                }
                state = string.Join(ViewSeparator, leaders);
            }
            catch (HttpRequestException error)
            {
                state = error.Message;
            }
            catch (JsonException error)
            {
                state = error.Message;
            }
            await Task.Delay(ReadinessPoll, TimeProvider.System, token);
        }
        throw new TimeoutException(NotReady + state);
    }

    /// <summary>The leader instance id in one node's gossip view, or empty while the election is incomplete.</summary>
    private static async Task<string> LeaderAsync(HttpClient node, int nodes, CancellationToken token)
    {
        using var response = await node.GetAsync(new Uri(GossipPath, UriKind.Relative), token);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var gossip = await JsonDocument.ParseAsync(body, cancellationToken: token);
        var members = gossip.RootElement.GetProperty(MembersField).EnumerateArray().ToArray();
        var leaders = Array.FindAll(members, member => HasState(member, LeaderState));
        var ready = members.Length == nodes && leaders.Length == ExpectedLeaders
            && Array.FindAll(members, member => HasState(member, FollowerState)).Length == nodes - ExpectedLeaders
            && Array.TrueForAll(members, member => member.GetProperty(AliveField).GetBoolean());
        return ready ? leaders[0].GetProperty(InstanceField).GetString() ?? string.Empty : string.Empty;
    }

    private static bool HasState(JsonElement member, string state)
        => string.Equals(member.GetProperty(StateField).GetString(), state, StringComparison.Ordinal);
}
