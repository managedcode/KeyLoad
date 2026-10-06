using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares primary key reads and writes, with optional direct-replica fsync receipts for the replicated profile.</summary>
/// <remarks>Creates a Redis key-value target with optional direct replica endpoints for receipt verification.</remarks>
/// <param name="connectionString">Connection settings for the single configured primary endpoint.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate benchmark keys.</param>
/// <param name="image">Redis image reference recorded after initialization and replica verification.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="nativeExecutionOptions">Centrally validated native adapter execution policy.</param>
/// <param name="diagnosticOptions">Centrally validated privacy-preserving diagnostic bounds.</param>
/// <param name="topology">The expected single-primary or direct-replica topology; replicated mode does not imply sharding or failover.</param>
/// <param name="replicas">Optional direct replica endpoints checked for the replicated durability receipt.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
public sealed class RedisTarget(string connectionString, string runId, string image,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions,
    IOptions<NativeComparisonDiagnosticOptions> diagnosticOptions,
    ComparisonTopology topology = ComparisonTopology.Standalone, string[]? replicas = null, TimeProvider? provider = null) : IComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private readonly IOptions<NativeComparisonExecutionOptions> executionOptions = NativeComparisonExecutionOptions.Require(nativeExecutionOptions);
    private readonly IOptions<NativeComparisonDiagnosticOptions> diagnostics = NativeComparisonDiagnosticOptions.Require(diagnosticOptions);
    private const string FieldSeparator = ":";

    private const string RunIdentityFormat = "N";

    private const string Prefix = "keyload-benchmark:";
    private const string TargetName = "Redis";
    private const string InitialVersion = "unverified";
    private const string InitialTopology = "unverified native topology";
    private const string InitialReadContract = "primary key reads";
    private const string TcpTransport = "RESP/TCP multiplexed";
    private const string TlsTransport = "RESP/TLS multiplexed";
    private const string Authorization = "Aspire password; no row/field policy";
    private const string ReplicatedTopology = "single primary plus two native direct replicas; no cluster sharding or automatic failover claim";
    private const string TwoNodeTopology = "single primary plus one native direct replica; no sharding or automatic failover claim";
    private const string SingleTopology = "single primary, no replicas";
    private const string AofAcknowledgement = "AOF appendfsync=always; single-node ACK";
    private const string ReplicatedAcknowledgement = "AOF appendfsync=always; WAITAOF 1 local + 1 replica fsync on the same primary connection (receipt RPC included)";
    private readonly string connectionSettings = connectionString;
    private readonly string prefix = Prefix + Guid.Parse(runId).ToString(RunIdentityFormat) + FieldSeparator;
    private readonly string imageName = image;
    private readonly ComparisonTopology configuredTopology = topology;
    private readonly string[] replicaEndpoints = replicas ?? [];
    private ConnectionMultiplexer? connection;
    private int corpusCount;

    /// <summary>Gets the observed version, direct-replica topology, fsync acknowledgement, read, transport, and authorization profile.</summary>
    public TargetProfile Profile { get; private set; } = new(TargetName, InitialVersion, InitialTopology,
        AofAcknowledgement, InitialReadContract, TcpTransport, Authorization, null);
    /// <summary>Reports support for primary point reads and document writes.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for point read or document write; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete;

    /// <summary>Connects to the configured primary, seeds run-specific keys, and verifies native topology and fsync receipts.</summary>
    /// <param name="dataset">The deterministic document corpus used to seed and probe the target.</param>
    /// <param name="cancellationToken">A token that cancels connection and replica-verification operations.</param>
    /// <returns>A task that completes after the observed Redis profile has been recorded.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        const string PrimaryKeyReadsS1SeedsDocumentKeysOnlyToken = "primary key reads; S1 seeds document keys only";
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        ArgumentNullException.ThrowIfNull(dataset);
        corpusCount = dataset.Documents.Count;
        if (dataset.Settings is ScaledComparisonProfile)
        {
            Profile = Profile with { ReadContract = PrimaryKeyReadsS1SeedsDocumentKeysOnlyToken };
        }
        var settings = RedisReplicaProof.CreateOptions(connectionSettings);
        if (settings.EndPoints.Count != SingleItemCount)
        {
            throw new ComparisonFailureException(RedisNativeProtocol.PrimaryEndpointError);
        }

        Profile = Profile with { Transport = settings.Ssl ? TlsTransport : TcpTransport };
        connection = await ConnectionMultiplexer.ConnectAsync(settings).WaitAsync(cancellationToken);
        var primaryEndpoint = RedisNativeProtocol.RequirePrimaryEndpoint(connection);
        var primaryIdentity = await RedisReplicaProof.ReadIdentityAsync(connection, primaryEndpoint, cancellationToken);
        var database = connection.GetDatabase();
        if (dataset.Settings is ScaledComparisonProfile)
        {
            await RedisScaledCorpusSeeder.SeedAsync(database, prefix, dataset.Documents, executionOptions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var document in dataset.Documents)
            {
                await database.StringSetAsync(prefix + document.Id, document.Json, flags: CommandFlags.DemandMaster).WaitAsync(cancellationToken);
            }
        }

        var probeKey = prefix + Guid.NewGuid().ToString(RunIdentityFormat);
        var evidence = await RedisReplicaProof.VerifyAsync(primary: connection, replicaStrings: replicaEndpoints,
            topology: configuredTopology, primaryIdentity: primaryIdentity, probeKey: probeKey,
            payload: dataset.Documents[FirstElementIndex].Json, token: cancellationToken, lifecycleOptions: lifecycleOptions, diagnosticOptions: diagnostics, timeProvider: timeProvider);
        Profile = Profile with
        {
            Version = primaryIdentity.Version,
            Topology = configuredTopology switch
            {
                ComparisonTopology.Replicated => ReplicatedTopology,
                ComparisonTopology.TwoNode => TwoNodeTopology,
                _ => SingleTopology
            },
            WriteAcknowledgement = ComparisonTopologies.NodeCount(configuredTopology) > SingleItemCount ? ReplicatedAcknowledgement : AofAcknowledgement,
            Image = imageName,
            Cluster = evidence
        };
    }

    /// <summary>Opens an independent Redis connection and verifies that its worker route reaches the expected primary.</summary>
    /// <param name="cancellationToken">A token that cancels worker connection and primary verification.</param>
    /// <returns>A session that owns the worker connection until disposed.</returns>
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        const int SingleItemCount = 1;

        var settings = RedisReplicaProof.CreateOptions(connectionSettings);
        if (settings.EndPoints.Count != SingleItemCount)
        {
            throw new ComparisonFailureException(RedisNativeProtocol.PrimaryEndpointError);
        }

        var workerConnection = await ConnectionMultiplexer.ConnectAsync(settings).WaitAsync(cancellationToken);
        try
        { await RedisReplicaProof.VerifyWorkerPrimaryAsync(workerConnection, configuredTopology, cancellationToken); }
        catch (Exception) { await workerConnection.DisposeAsync(); throw; }
        return new RedisComparisonSession(workerConnection, prefix, configuredTopology, corpusCount, lifecycleOptions, executionOptions);
    }

    /// <summary>Closes and disposes the target-owned Redis connection.</summary>
    /// <returns>A value task that completes after the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        { await connection.CloseAsync(); connection.Dispose(); }
    }

}
