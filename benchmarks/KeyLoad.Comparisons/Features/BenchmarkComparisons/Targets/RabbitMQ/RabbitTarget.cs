using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares the enqueue, receive, and manual acknowledgement cycle on a run-specific RabbitMQ quorum queue.</summary>
/// <remarks>Creates a RabbitMQ queue-cycle target and its optional management API client for topology verification.</remarks>
/// <param name="connectionString">AMQP connection URI used to create the broker connection.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate the queue name.</param>
/// <param name="image">Broker image reference recorded in the verified profile.</param>
/// <param name="topology">The expected one-, two- or three-member quorum queue topology.</param>
/// <param name="management">Optional management API client for broker membership verification; initialization requires it, and the target disposes it.</param>
public sealed class RabbitTarget(string connectionString, string runId, string image,
    ComparisonTopology topology = ComparisonTopology.Standalone, HttpClient? management = null) : IComparisonTarget
{
    private const string QueuePrefix = "keyload_benchmark_";
    private readonly string brokerConnectionString = connectionString;
    private readonly string queue = QueuePrefix + Guid.Parse(runId).ToString("N");
    private readonly string imageName = image;
    private readonly ComparisonTopology configuredTopology = topology;
    private readonly Dictionary<string, object?> queueArguments = RabbitNativePolicy.QueueArguments(topology);
    private readonly HttpClient? managementClient = management;
    private IConnection? connection;

    /// <summary>Gets the observed broker version and topology plus publisher-confirm and manual-acknowledgement profile.</summary>
    public TargetProfile Profile { get; private set; } = new("RabbitMQ", "unverified", "unverified native topology",
        "persistent messages + tracked publisher confirms; manual consumer ACK + channel RPC barrier",
        "basic.get manual acknowledgements; at least once", "AMQP 0-9-1/TCP; one channel per worker", "Aspire user; no field policy", null);
    /// <summary>Reports support only for the queue-cycle scenario.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> only when <paramref name="scenario"/> is queue cycle.</returns>
    public bool Supports(Scenario scenario) => scenario == Scenario.QueueCycle;

    /// <summary>Declares a durable quorum queue, verifies broker membership, and checks persistent publish, receive, and acknowledgement behavior.</summary>
    /// <param name="corpus">The common target-contract corpus parameter; queue setup does not use it.</param>
    /// <param name="cancellationToken">A token that cancels connection, queue, and management operations.</param>
    /// <returns>A task that completes after queue semantics and cluster evidence are verified.</returns>
    public async Task InitializeAsync(IComparisonCorpus corpus, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(brokerConnectionString);
        var factory = new ConnectionFactory { Uri = endpoint, AutomaticRecoveryEnabled = false };
        connection = await factory.CreateConnectionAsync(cancellationToken);
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: queueArguments, cancellationToken: cancellationToken);
        }
        if (managementClient is null)
        {
            throw new ComparisonFailureException("RabbitManagementClientRequired");
        }

        var proof = await RabbitReplicaProof.VerifyAsync(managementClient, queue, configuredTopology, cancellationToken);
        await ProbeQueueAsync(cancellationToken);
        Profile = Profile with
        {
            Version = proof.Version,
            Topology = RabbitNativePolicy.TopologyLabel(configuredTopology),
            WriteAcknowledgement = $"persistent messages + tracked publisher confirms; quorum {ComparisonTopologies.NodeCount(configuredTopology) / 2 + 1} of {ComparisonTopologies.NodeCount(configuredTopology)}; quorum member online verification; manual ACK + ordered channel RPC barrier",
            Transport = endpoint.Scheme == "amqps" ? "AMQP 0-9-1/TLS; one channel per worker" : "AMQP 0-9-1/TCP; one channel per worker",
            Image = imageName,
            Cluster = proof.Evidence
        };
    }

    private async Task ProbeQueueAsync(CancellationToken cancellationToken)
    {
        await using var channel = await connection!.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        var probeId = Guid.NewGuid().ToString("N");
        await channel.BasicPublishAsync("", queue, mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, MessageId = probeId },
            body: Encoding.UTF8.GetBytes(probeId), cancellationToken: cancellationToken);
        var delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
        if (delivery is null || delivery.BasicProperties.MessageId != probeId || Encoding.UTF8.GetString(delivery.Body.Span) != probeId)
        {
            throw new ComparisonFailureException("RabbitConfirmedQueueProbeFailed");
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
    }

    /// <summary>Opens an independent publisher-confirm channel for a queue-cycle session.</summary>
    /// <param name="cancellationToken">A token that cancels creation of the session channel.</param>
    /// <returns>A session that owns and later disposes its channel.</returns>
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => new Session(await connection!.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken), queue);

    /// <summary>Deletes the run-specific queue, then disposes the broker connection and management client.</summary>
    /// <returns>A value task that completes after target cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (connection is null)
        { managementClient?.Dispose(); return; }
        try
        {
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeleteAsync(queue);
        }
        finally { await connection.DisposeAsync(); managementClient?.Dispose(); }
    }

    /// <summary>Measures one persistent publish, manual receive, and acknowledgement cycle on its owned channel.</summary>
    private sealed class Session(IChannel channel, string queue) : IComparisonSession
    {
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (scenario != Scenario.QueueCycle)
            {
                throw new NotSupportedException();
            }

            var begin = Stopwatch.GetTimestamp();
            await channel.BasicPublishAsync("", queue, mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, MessageId = document.Id, ContentType = "application/json" },
                body: Encoding.UTF8.GetBytes(document.Json), cancellationToken: cancellationToken);
            var enqueued = Stopwatch.GetTimestamp();
            BasicGetResult? delivery = null;
            while (delivery is null)
            {
                delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
                if (delivery is null)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
            var received = Stopwatch.GetTimestamp();
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
            return new(Message: new(delivery.BasicProperties.MessageId!, Encoding.UTF8.GetString(delivery.Body.Span)), Queue: new(
                Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds, Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds,
                Stopwatch.GetElapsedTime(received).TotalMilliseconds));
        }

        public ValueTask DisposeAsync() => channel.DisposeAsync();
    }
}
