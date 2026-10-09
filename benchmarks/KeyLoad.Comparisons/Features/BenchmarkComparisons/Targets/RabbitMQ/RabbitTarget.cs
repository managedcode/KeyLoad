using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares the enqueue, receive, and manual acknowledgement cycle on a run-specific RabbitMQ quorum queue.</summary>
/// <remarks>Creates a RabbitMQ queue-cycle target and its optional management API client for topology verification.</remarks>
/// <param name="connectionString">AMQP connection URI used to create the broker connection.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate the queue name.</param>
/// <param name="image">Broker image reference recorded in the verified profile.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="topology">The expected one- or three-member quorum queue topology.</param>
/// <param name="management">Optional management API client for broker membership verification; initialization requires it, and the target disposes it.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
public sealed class RabbitTarget(string connectionString, string runId, string image,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions,
    ComparisonTopology topology = ComparisonTopology.Standalone, HttpClient? management = null, TimeProvider? provider = null) : IComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string InitializeAsyncPersistentMessagesTrackedPublisherConfirmsQuorumText = "persistent messages + tracked publisher confirms; quorum ";
    private const string InitializeAsyncOfText = " of ";
    private const string InitializeAsyncQuorumMemberOnlineVerificationManualACKOrderedChannelRPCBarrierText = "; quorum member online verification; manual ACK + ordered channel RPC barrier";

    private const string UnverifiedToken = "unverified";
    private const string UnverifiedNativeTopologyToken = "unverified native topology";
    private const string PublisherConfirmAndConsumerAcknowledgementContract = "persistent messages + tracked publisher confirms; manual consumer ACK + channel RPC barrier";
    private const string BasicGetManualAcknowledgementsAtLeastOnceToken = "basic.get manual acknowledgements; at least once";
    private const string AMQPTCPOneChannelPerWorkerToken = "AMQP 0-9-1/TCP; one channel per worker";
    private const string AspireUserNoFieldPolicyToken = "Aspire user; no field policy";

    private const string RunIdentityFormat = "N";
    private const string TlsScheme = "amqps";

    private const string QueuePrefix = "keyload_benchmark_";
    private readonly string brokerConnectionString = connectionString;
    private readonly string queue = QueuePrefix + Guid.Parse(runId).ToString(RunIdentityFormat);
    private readonly string imageName = image;
    private readonly ComparisonTopology configuredTopology = topology;
    private readonly Dictionary<string, object?> queueArguments = RabbitNativePolicy.QueueArguments(topology);
    private readonly HttpClient? managementClient = management;
    private IConnection? connection;

    /// <summary>Gets the observed broker version and topology plus publisher-confirm and manual-acknowledgement profile.</summary>
    public TargetProfile Profile { get; private set; } = new(nameof(RabbitMQ), UnverifiedToken, UnverifiedNativeTopologyToken,
        PublisherConfirmAndConsumerAcknowledgementContract,
        BasicGetManualAcknowledgementsAtLeastOnceToken, AMQPTCPOneChannelPerWorkerToken, AspireUserNoFieldPolicyToken, null);
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
        const string RabbitManagementClientRequiredDetail = "RabbitManagementClientRequired";
        const int ClusterNodeCountMultiplier = 2;
        const int AdjacentElementOffset = 1;
        const string AMQPTLSOneChannelPerWorkerToken = "AMQP 0-9-1/TLS; one channel per worker";
        const string AMQPTCPOneChannelPerWorkerToken = "AMQP 0-9-1/TCP; one channel per worker";

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
            throw new ComparisonFailureException(RabbitManagementClientRequiredDetail);
        }

        var proof = await RabbitReplicaProof.VerifyAsync(management: managementClient, queue: queue, topology: configuredTopology,
            cancellationToken: cancellationToken, lifecycleOptions: lifecycleOptions, timeProvider: timeProvider);
        await ProbeQueueAsync(cancellationToken);
        Profile = Profile with
        {
            Version = proof.Version,
            Topology = RabbitNativePolicy.TopologyLabel(configuredTopology),
            WriteAcknowledgement = $"{InitializeAsyncPersistentMessagesTrackedPublisherConfirmsQuorumText}{ComparisonTopologies.NodeCount(configuredTopology) / ClusterNodeCountMultiplier + AdjacentElementOffset}{InitializeAsyncOfText}{ComparisonTopologies.NodeCount(configuredTopology)}{InitializeAsyncQuorumMemberOnlineVerificationManualACKOrderedChannelRPCBarrierText}",
            Transport = endpoint.Scheme == TlsScheme ? AMQPTLSOneChannelPerWorkerToken : AMQPTCPOneChannelPerWorkerToken,
            Image = imageName,
            Cluster = proof.Evidence
        };
    }

    private async Task ProbeQueueAsync(CancellationToken cancellationToken)
    {
        const string EmptyText = "";
        const string RabbitConfirmedQueueProbeFailedDetail = "RabbitConfirmedQueueProbeFailed";

        await using var channel = await connection!.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        var probeId = Guid.NewGuid().ToString(RunIdentityFormat);
        await channel.BasicPublishAsync(EmptyText, queue, mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, MessageId = probeId },
            body: Encoding.UTF8.GetBytes(probeId), cancellationToken: cancellationToken);
        var delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
        if (delivery is null || delivery.BasicProperties.MessageId != probeId || Encoding.UTF8.GetString(delivery.Body.Span) != probeId)
        {
            throw new ComparisonFailureException(RabbitConfirmedQueueProbeFailedDetail);
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
    }

    /// <summary>Opens an independent publisher-confirm channel for a queue-cycle session.</summary>
    /// <param name="cancellationToken">A token that cancels creation of the session channel.</param>
    /// <returns>A session that owns and later disposes its channel.</returns>
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => new Session(await connection!.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken), queue,
            lifecycleOptions.Value.QueueClaimPollInterval, timeProvider);

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
    private sealed class Session(IChannel channel, string queue, TimeSpan claimPollInterval, TimeProvider timeProvider) : IComparisonSession
    {
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            const string EmptyText = "";
            const string ApplicationJsonToken = "application/json";

            if (scenario != Scenario.QueueCycle)
            {
                throw new NotSupportedException();
            }

            var begin = timeProvider.GetTimestamp();
            await channel.BasicPublishAsync(EmptyText, queue, mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, MessageId = document.Id, ContentType = ApplicationJsonToken },
                body: Encoding.UTF8.GetBytes(document.Json), cancellationToken: cancellationToken);
            var enqueued = timeProvider.GetTimestamp();
            BasicGetResult? delivery = null;
            while (delivery is null)
            {
                delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
                if (delivery is null)
                {
                    await Task.Delay(claimPollInterval, timeProvider, cancellationToken);
                }
            }
            var received = timeProvider.GetTimestamp();
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
            return new(Message: new(delivery.BasicProperties.MessageId!, Encoding.UTF8.GetString(delivery.Body.Span)), Queue: new(
                timeProvider.GetElapsedTime(begin, enqueued).TotalMilliseconds, timeProvider.GetElapsedTime(enqueued, received).TotalMilliseconds,
                timeProvider.GetElapsedTime(received).TotalMilliseconds));
        }

        public ValueTask DisposeAsync() => channel.DisposeAsync();
    }
}
