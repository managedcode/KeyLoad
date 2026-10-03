using System.Collections.Concurrent;
using System.Text;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares event-stream append and read operations against a KurrentDB cluster with pre-measurement topology evidence.</summary>
public sealed class KurrentTarget : IComparisonTarget
{
    private readonly string connectionString;
    private readonly HttpClient[] nodeHttpClients;
    private readonly string runId;
    private readonly ComparisonTopology topology;
    private readonly ConcurrentDictionary<string, byte> ownedStreams = new(StringComparer.Ordinal);
    private readonly List<KurrentDBClient> ownedClients = [];
    private KurrentDBClient? writer;
    private KurrentDBClient[] nodeClients = [];
    private bool initialized;

    /// <summary>Gets the observed KurrentDB version, topology, acknowledgement, read, transport, and authorization profile.</summary>
    public TargetProfile Profile { get; private set; }

    /// <summary>Creates a KurrentDB stream target that verifies the requested native topology before measurement.</summary>
    /// <param name="connectionString">Connection settings used to create the writer and verify the cluster.</param>
    /// <param name="nodeClients">HTTP clients for the configured KurrentDB nodes; the target disposes these clients.</param>
    /// <param name="runId">Guid-formatted run identifier used to isolate benchmark stream names.</param>
    /// <param name="image">Pinned server image reference recorded in the target profile.</param>
    /// <param name="topology">The one, two or three native members that cluster verification must establish.</param>
    public KurrentTarget(string connectionString, HttpClient[] nodeClients, string runId, string image,
        ComparisonTopology topology)
    {
        ArgumentNullException.ThrowIfNull(image);
        this.connectionString = connectionString;
        nodeHttpClients = nodeClients;
        this.runId = Guid.Parse(runId).ToString(KurrentConstants.GuidFormat);
        this.topology = topology;
        Profile = CreateProfile(connectionString, image, topology);
    }

    /// <summary>Reports support only for stream append and stream read operations.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for stream append or read; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.StreamAppend or Scenario.StreamRead;

    /// <summary>Verifies the cluster and one-event stream semantics, seeds isolated streams, and captures replica-copy evidence.</summary>
    /// <param name="dataset">The deterministic corpus and timeout used to seed and verify streams.</param>
    /// <param name="cancellationToken">A token that cancels connection, verification, and seeding operations.</param>
    /// <returns>A task that completes after profile evidence has been recorded.</returns>
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        writer = new KurrentDBClient(KurrentNativeSettings.CreateWriter(connectionString));
        ownedClients.Add(writer);
        var timeout = TimeSpan.FromSeconds(dataset.Options.TimeoutSeconds);
        var proof = await KurrentClusterVerifier.VerifyAsync(connectionString, nodeHttpClients, topology, timeout, cancellationToken);
        nodeClients = proof.NodeClients;
        ownedClients.AddRange(nodeClients);
        await VerifyNoStreamConflictAsync(cancellationToken);

        foreach (var document in dataset.Documents)
        {
            var stream = StreamName(document);
            ownedStreams.TryAdd(stream, KurrentConstants.OwnedStreamMarker);
            await RequireWriter().AppendToStreamAsync(stream, StreamState.NoStream,
                [CreateEvent(document)], cancellationToken: cancellationToken);
        }
        var probe = StreamName(KurrentConstants.ProbeStreamSuffix);
        ownedStreams.TryAdd(probe, KurrentConstants.OwnedStreamMarker);
        var eventData = CreateProbeEvent();
        var evidence = await KurrentClusterVerifier.VerifyCopyAsync(RequireWriter(), nodeClients, nodeHttpClients,
            topology, probe, eventData, timeout, cancellationToken);
        Profile = Profile with { Cluster = evidence };
        initialized = true;
    }

    /// <summary>Opens a stream comparison session after successful target initialization.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; opening the in-memory session does not perform I/O.</param>
    /// <returns>A session that appends and reads events through the verified writer client.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        if (!initialized)
        {
            throw new ComparisonFailureException(KurrentConstants.NotInitialized);
        }

        return Task.FromResult<IComparisonSession>(new KurrentSession(RequireWriter(), this));
    }

    /// <summary>Deletes streams created for this run and disposes the target-owned KurrentDB and HTTP clients.</summary>
    /// <returns>A value task that completes when cleanup and client disposal finish.</returns>
    public async ValueTask DisposeAsync()
    {
        using var cleanup = new KurrentCleanupOperation(ownedStreams.Keys.ToArray(), CancellationToken.None);
        try
        {
            await cleanup.DeleteAsync(writer);
        }
        finally
        {
            var otherClients = ownedClients.Where(client => !ReferenceEquals(client, writer)).ToArray();
            var disposals = cleanup.StartDisposals(otherClients, nodeHttpClients);
            var writerDisposal = Task.CompletedTask;
            try
            {
                if (writer is not null)
                {
                    writerDisposal = writer.DisposeAsync().AsTask();
                }
            }
            finally
            {
                try
                {
                    await cleanup.FinishAsync(disposals, writerDisposal);
                }
                finally
                {
                    ownedClients.Clear();
                }
            }
        }
    }

    internal string StreamName(BenchmarkDocument document) => StreamName(document.Id);
    internal string StreamName(string suffix) => KurrentConstants.StreamPrefix + runId + KurrentConstants.StreamSeparator + suffix;
    internal void TrackStream(string stream) => ownedStreams.TryAdd(stream, KurrentConstants.OwnedStreamMarker);

    internal static KurrentEventData CreateEvent(BenchmarkDocument document)
        => new(Uuid.FromGuid(BenchmarkDataset.EventId(document)), KurrentConstants.EventType,
            Encoding.UTF8.GetBytes(document.Json), contentType: KurrentConstants.EventJson);

    private static KurrentEventData CreateProbeEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType,
            Encoding.UTF8.GetBytes(KurrentConstants.ProbePayload), contentType: KurrentConstants.EventJson);

    private async Task VerifyNoStreamConflictAsync(CancellationToken cancellationToken)
    {
        var stream = StreamName(KurrentConstants.SemanticsProbeSuffix);
        ownedStreams.TryAdd(stream, KurrentConstants.OwnedStreamMarker);
        var firstEvent = CreateProbeEvent();
        await RequireWriter().AppendToStreamAsync(stream, StreamState.NoStream,
            [firstEvent],
            cancellationToken: cancellationToken);
        try
        {
            await RequireWriter().AppendToStreamAsync(stream, StreamState.NoStream,
                [CreateProbeEvent()],
                cancellationToken: cancellationToken);
            throw new ComparisonFailureException(KurrentConstants.DuplicateStreamAccepted);
        }
        catch (WrongExpectedVersionException)
        {
            // A different event ID cannot create a second event under the NoStream precondition.
        }
        await KurrentReplicaProbe.RequireOriginalEventAsync(RequireWriter(), stream, firstEvent, cancellationToken);
    }

    private static TargetProfile CreateProfile(string connectionString, string image, ComparisonTopology topology)
    {
        var imageName = image.Split(KurrentConstants.ImageDigestSeparator, KurrentConstants.ImageReferenceParts)[0];
        var imageTag = imageName.Split(KurrentConstants.ImagePathSeparator).Last().Split(KurrentConstants.ImageTagSeparator).Last();
        if (imageTag != KurrentConstants.ExpectedServerVersion)
        {
            throw new ComparisonFailureException(KurrentConstants.VersionMismatch);
        }

        var settings = KurrentNativeSettings.CreateWriter(connectionString);
        var connectivity = settings.ConnectivitySettings;
        var insecure = connectivity.Insecure;
        var transport = insecure ? KurrentConstants.Insecure : KurrentConstants.TlsVerified;
        var credentialsConfigured = settings.DefaultCredentials is not null;
        var clientCertificateConfigured = connectivity.ClientCertificate is not null;
        var authorization = insecure
            ? credentialsConfigured ? KurrentConstants.InsecureCredentialsIgnored : KurrentConstants.Unauthenticated
            : credentialsConfigured ? KurrentConstants.Authenticated : KurrentConstants.Unauthenticated;
        var certificateMetadata = clientCertificateConfigured
            ? insecure ? KurrentConstants.TlsClientCertificateIgnored : KurrentConstants.TlsClientCertificateConfigured
            : KurrentConstants.TlsClientCertificateAbsent;
        var profile = new TargetProfile(KurrentConstants.Name, KurrentConstants.ExpectedServerVersion,
            ComparisonTopologies.NodeCount(topology) == 1 ? KurrentConstants.SingleTopology :
                topology == ComparisonTopology.TwoNode ? KurrentConstants.TwoNodeTopology : KurrentConstants.ReplicatedTopology,
            ComparisonTopologies.NodeCount(topology) > 1 ? KurrentConstants.ReplicatedAcknowledgement : KurrentConstants.SingleAcknowledgement,
            KurrentConstants.ReadContract + KurrentConstants.WriterPreferenceLabel,
            transport, authorization + KurrentConstants.AuthorizationSeparator + certificateMetadata + KurrentConstants.AuthorizationSeparator + KurrentConstants.CommunityAuthorization, image);
        return profile;
    }

    private KurrentDBClient RequireWriter() => writer ?? throw new ComparisonFailureException(KurrentConstants.NotInitialized);
}
