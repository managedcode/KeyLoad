using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;
using RabbitMQ.Client;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>
/// RabbitMQ through RabbitMQ.Client over AMQP 0-9-1: one durable quorum queue with a member on every node, persistent
/// messages with tracked publisher confirms, <c>basic.get</c> with manual <c>basic.ack</c> followed by a channel RPC,
/// all on pooled channels; a delivery keeps its channel until it is acknowledged.
/// </summary>
internal sealed class RabbitQueueDriver(BenchmarkConnection connection) : IQueueDriver
{
    private const string QueuePrefix = "keyload_benchmark_";
    private const string QueueFormat = "N";
    private const string QueueTypeArgument = "x-queue-type";
    private const string GroupSizeArgument = "x-quorum-initial-group-size";
    private const string QuorumType = "quorum";
    private const string DefaultExchange = "";
    private const string NodesPath = "api/nodes";
    private const string QueuePath = "api/queues/%2F/";
    private const string RunningField = "running";
    private const string MembersField = "members";
    private const string OnlineField = "online";
    private const int ManagementNode = 0;
    private const int ReceiveSeconds = 60;
    private const string NoDelivery = "No RabbitMQ delivery arrived within 60 seconds.";
    private const string MissingId = "RabbitMQ delivered a message without a message id.";
    private const string WrongHandle = "The delivery was not received by the RabbitMQ queue driver.";
    private const string NotPrepared = "Prepare the RabbitMQ queue before using it.";
    private const string NotReady = "RabbitMQ did not reach the expected cluster state at ";
    private const string StateSeparator = ": ";
    private static readonly TimeSpan ReceiveLimit = TimeSpan.FromSeconds(ReceiveSeconds);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ReadinessPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);
    private readonly string queue = QueuePrefix + Guid.NewGuid().ToString(QueueFormat);
    private readonly ConcurrentBag<IChannel> idle = new();
    private readonly ConcurrentDictionary<IChannel, bool> open = new();
    private IConnection? amqp;

    private IConnection Amqp => amqp ?? throw new InvalidOperationException(NotPrepared);

    /// <summary>Waits for every node, declares the quorum queue and waits until all its members are online.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        using var management = connection.CreateHttpClient(ManagementNode);
        await WaitAsync(management, NodesPath, nodes => nodes.EnumerateArray().Count(IsRunning) == connection.Nodes, token);
        var factory = new ConnectionFactory { Uri = new Uri(connection.RequireConnectionString()), AutomaticRecoveryEnabled = false };
        amqp = await factory.CreateConnectionAsync(token);
        await DeclareAsync(token);
        await WaitAsync(management, QueuePath + Uri.EscapeDataString(queue),
            detail => ArrayLength(detail, MembersField) == connection.Nodes && ArrayLength(detail, OnlineField) == connection.Nodes, token);
    }

    public async Task PublishAsync(QueueMessage message, CancellationToken token)
    {
        var channel = await RentAsync(token);
        var published = false;
        try
        {
            await channel.BasicPublishAsync(DefaultExchange, queue, mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, MessageId = message.Id },
                body: Encoding.UTF8.GetBytes(message.Body), cancellationToken: token);
            published = true;
        }
        finally
        {
            await ReleaseAsync(channel, published);
        }
    }

    public async Task<QueueDelivery> ReceiveAsync(CancellationToken token)
    {
        var channel = await RentAsync(token);
        var held = false;
        try
        {
            var result = await GetAsync(channel, token);
            var message = new QueueMessage(result.BasicProperties.MessageId ?? throw new InvalidDataException(MissingId),
                Encoding.UTF8.GetString(result.Body.Span));
            held = true;
            return new(message, new RabbitReceipt(channel, result.DeliveryTag));
        }
        finally
        {
            if (!held)
            {
                await ReleaseAsync(channel, healthy: false);
            }
        }
    }

    /// <summary>Acknowledges on the delivering channel; the passive declare returns after the broker processed the ack.</summary>
    public async Task AckAsync(QueueDelivery delivery, CancellationToken token)
    {
        if (delivery.Handle is not RabbitReceipt receipt)
        {
            throw new ArgumentException(WrongHandle, nameof(delivery));
        }
        var acknowledged = false;
        try
        {
            await receipt.Channel.BasicAckAsync(receipt.Tag, multiple: false, token);
            await receipt.Channel.QueueDeclarePassiveAsync(queue, token);
            acknowledged = true;
        }
        finally
        {
            await ReleaseAsync(receipt.Channel, acknowledged);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in open.Keys)
        {
            await channel.DisposeAsync();
        }
        if (amqp is not null)
        {
            await amqp.DisposeAsync();
        }
    }

    private async Task DeclareAsync(CancellationToken token)
    {
        await using var channel = await Amqp.CreateChannelAsync(cancellationToken: token);
        var arguments = new Dictionary<string, object?> { [QueueTypeArgument] = QuorumType, [GroupSizeArgument] = connection.Nodes };
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments,
            cancellationToken: token);
    }

    private async Task<BasicGetResult> GetAsync(IChannel channel, CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < ReceiveLimit)
        {
            if (await channel.BasicGetAsync(queue, autoAck: false, token) is { } result)
            {
                return result;
            }
            await Task.Delay(PollDelay, TimeProvider.System, token);
        }
        throw new TimeoutException(NoDelivery);
    }

    private async ValueTask<IChannel> RentAsync(CancellationToken token)
    {
        while (idle.TryTake(out var pooled))
        {
            if (pooled.IsOpen)
            {
                return pooled;
            }
            await ReleaseAsync(pooled, healthy: false);
        }
        var channel = await Amqp.CreateChannelAsync(new CreateChannelOptions(true, true), token);
        open.TryAdd(channel, true);
        return channel;
    }

    /// <summary>Returns a healthy channel to the pool; closes one whose operation failed.</summary>
    private async ValueTask ReleaseAsync(IChannel channel, bool healthy)
    {
        if (healthy)
        {
            idle.Add(channel);
            return;
        }
        open.TryRemove(channel, out _);
        await channel.DisposeAsync();
    }

    private static async Task WaitAsync(HttpClient management, string path, Func<JsonElement, bool> ready, CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        var state = string.Empty;
        while (TimeProvider.System.GetElapsedTime(started) < ReadinessLimit)
        {
            try
            {
                using var response = await management.GetAsync(new Uri(path, UriKind.Relative), token);
                state = await response.Content.ReadAsStringAsync(token);
                if (response.IsSuccessStatusCode && Matches(state, ready))
                {
                    return;
                }
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
        throw new TimeoutException(NotReady + path + StateSeparator + state);
    }

    private static bool Matches(string json, Func<JsonElement, bool> ready)
    {
        using var document = JsonDocument.Parse(json);
        return ready(document.RootElement);
    }

    private static bool IsRunning(JsonElement node)
        => node.TryGetProperty(RunningField, out var running) && running.ValueKind == JsonValueKind.True;

    private static int ArrayLength(JsonElement detail, string field)
        => detail.TryGetProperty(field, out var items) && items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;

    /// <summary>A delivery tag and the channel it belongs to; the ack must go on that channel.</summary>
    private sealed record RabbitReceipt(IChannel Channel, ulong Tag);
}
