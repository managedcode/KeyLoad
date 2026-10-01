using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed class RabbitTarget(string connectionString, string runId, string image) : IComparisonTarget
{
    private readonly string queue = "keyload_benchmark_" + Guid.Parse(runId).ToString("N");
    private IConnection? connection;
    public TargetProfile Profile { get; private set; } = new("RabbitMQ", "unverified", "single broker; quorum queue with one member, RF1",
        "persistent messages + tracked publisher confirms; manual consumer ACK + channel RPC barrier",
        "basic.get manual acknowledgements; at least once", "AMQP 0-9-1/TCP; one channel per worker", "Aspire user; no field policy", image);
    public bool Supports(Scenario scenario) => scenario == Scenario.QueueCycle;
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(connectionString);
        Profile = Profile with { Transport = endpoint.Scheme == "amqps" ? "AMQP 0-9-1/TLS; one channel per worker" : "AMQP 0-9-1/TCP; one channel per worker" };
        var factory = new ConnectionFactory { Uri = endpoint, AutomaticRecoveryEnabled = false };
        connection = await factory.CreateConnectionAsync(cancellationToken);
        if (connection.ServerProperties?.TryGetValue("version", out var version) == true && version is byte[] bytes)
            Profile = Profile with { Version = Encoding.UTF8.GetString(bytes) };
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-quorum-initial-group-size"] = 1 }, cancellationToken: cancellationToken);
    }
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => new Session(await connection!.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken), queue);
    public async ValueTask DisposeAsync()
    {
        if (connection is null) return;
        try
        {
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeleteAsync(queue);
        }
        finally { await connection.DisposeAsync(); }
    }
    private sealed class Session(IChannel channel, string queue) : IComparisonSession
    {
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (scenario != Scenario.QueueCycle) throw new NotSupportedException();
            var begin = Stopwatch.GetTimestamp();
            await channel.BasicPublishAsync("", queue, mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, MessageId = document.Id, ContentType = "application/json" },
                body: Encoding.UTF8.GetBytes(document.Json), cancellationToken: cancellationToken);
            // With tracked confirmations enabled, BasicPublishAsync waits for broker confirmation.
            var enqueued = Stopwatch.GetTimestamp();
            BasicGetResult? delivery = null;
            while (delivery is null)
            {
                delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
                if (delivery is null) await Task.Delay(1, cancellationToken);
            }
            var received = Stopwatch.GetTimestamp();
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            // AMQP basic.ack has no server response. This ordered RPC waits for broker processing on this channel.
            await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
            return new(Message: new(delivery.BasicProperties.MessageId!, Encoding.UTF8.GetString(delivery.Body.Span)), Queue: new(
                Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds, Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds,
                Stopwatch.GetElapsedTime(received).TotalMilliseconds));
        }
        public ValueTask DisposeAsync() => channel.DisposeAsync();
    }
}
