using System.Text;
using Microsoft.Extensions.Options;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares event-stream append and read operations against a KurrentDB cluster with pre-measurement topology evidence.</summary>
public sealed class KurrentTarget : IComparisonTarget
{
    private readonly IOptions<ComparisonLifecycleOptions> lifecycleOptions;
    private readonly string connectionString;
    private readonly HttpClient[] nodeHttpClients;
    private readonly string runId;
    private readonly ComparisonTopology topology;
    private KurrentStreamOwnership? ownership;
    private readonly List<KurrentDBClient> ownedClients = [];
    private KurrentDBClient? writer;
    private KurrentDBClient[] nodeClients = [];
    private bool initialized;
    private KurrentSetupStage setupStage = KurrentSetupStage.WriterConstruction;

    /// <summary>Gets the observed KurrentDB version, topology, acknowledgement, read, transport, and authorization profile.</summary>
    public TargetProfile Profile { get; private set; }

    /// <summary>Creates a KurrentDB stream target that verifies the requested native topology before measurement.</summary>
    /// <param name="connectionString">Connection settings used to create the writer and verify the cluster.</param>
    /// <param name="nodeClients">HTTP clients for the configured KurrentDB nodes; the target disposes these clients.</param>
    /// <param name="runId">Guid-formatted run identifier used to isolate benchmark stream names.</param>
    /// <param name="image">Pinned server image reference recorded in the target profile.</param>
    /// <param name="lifecycleOptions">The validated native lifecycle policy.</param>
    /// <param name="topology">The one, two or three native members that cluster verification must establish.</param>
    public KurrentTarget(string connectionString, HttpClient[] nodeClients, string runId, string image,
        ComparisonTopology topology, IOptions<ComparisonLifecycleOptions> lifecycleOptions)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(lifecycleOptions);
        lifecycleOptions.Value.Validate();
        this.lifecycleOptions = lifecycleOptions;
        this.connectionString = connectionString;
        nodeHttpClients = nodeClients;
        this.runId = Guid.Parse(runId).ToString(KurrentConstants.GuidFormat);
        this.topology = topology;
        Profile = KurrentTargetProfile.Create(connectionString, image, topology);
    }

    /// <summary>Reports support only for stream append and stream read operations.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for stream append or read; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.StreamAppend or Scenario.StreamRead;

    /// <summary>Verifies the cluster and one-event stream semantics, seeds isolated streams, and captures replica-copy evidence.</summary>
    /// <param name="corpus">The deterministic corpus and timeout used to seed and verify streams.</param>
    /// <param name="cancellationToken">A token that cancels connection, verification, and seeding operations.</param>
    /// <returns>A task that completes after profile evidence has been recorded.</returns>
    public async Task InitializeAsync(IComparisonCorpus corpus, CancellationToken cancellationToken)
    {
        const string ThisTargetDoesNotSupportTheBoundedScaledDocumentCorpusDetail = "This target does not support the bounded scaled document corpus.";

        ArgumentNullException.ThrowIfNull(corpus);
        if (corpus is not BenchmarkDataset dataset)
        {
            throw new NotSupportedException(ThisTargetDoesNotSupportTheBoundedScaledDocumentCorpusDetail);
        }
        if (ownership is not null)
        {
            throw new ComparisonFailureException(KurrentConstants.OwnershipAlreadyInitialized);
        }
        try
        {
            ownership = new KurrentStreamOwnership(dataset.ExecutionOptions);
            setupStage = KurrentSetupStage.MemberVerification;
            var timeout = TimeSpan.FromSeconds(dataset.Options.TimeoutSeconds);
            var proof = await KurrentClusterVerifier.VerifyAsync(connectionString: connectionString, httpClients: nodeHttpClients,
                topology: topology, timeout: timeout, cancellationToken: cancellationToken, options: lifecycleOptions);
            nodeClients = proof.NodeClients;
            ownedClients.AddRange(nodeClients);
            // Native SDK construction eagerly discovers and caches a preferred live member.
            // Construct the writer after all native views agree on their actual leader.
            setupStage = KurrentSetupStage.WriterConstruction;
            writer = new KurrentDBClient(KurrentNativeSettings.CreateWriter(connectionString));
            ownedClients.Add(writer);
            setupStage = KurrentSetupStage.NoStreamSemantics;
            await VerifyNoStreamConflictAsync(cancellationToken);

            setupStage = KurrentSetupStage.CorpusSeeding;
            foreach (var document in dataset.Documents)
            {
                var stream = StreamName(document);
                await KurrentOwnedStreamAppend.AppendAsync(RequireWriter(), ownership, stream, CreateEvent(document), cancellationToken);
            }
            setupStage = KurrentSetupStage.ReplicaCopy;
            var probe = StreamName(KurrentConstants.ProbeStreamSuffix);
            var eventData = CreateProbeEvent();
            var evidence = await KurrentClusterVerifier.VerifyCopyAsync(writer: RequireWriter(), nodeClients: nodeClients,
                httpClients: nodeHttpClients, topology: topology, stream: probe, eventData: eventData, ownership: ownership,
                timeout: timeout, cancellationToken: cancellationToken, options: lifecycleOptions);
            Profile = Profile with { Cluster = evidence };
            initialized = true;
            setupStage = KurrentSetupStage.Complete;
        }
        catch (Exception failure)
        {
            KurrentSetupDiagnostics.TryWrite(setupStage, failure);
            throw;
        }
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
        using var cleanup = new KurrentCleanupOperation(streams: ownership?.SnapshotAcknowledged() ?? [], token: CancellationToken.None,
            options: lifecycleOptions);
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
    internal KurrentStreamOwnership Ownership => ownership ?? throw new ComparisonFailureException(KurrentConstants.NotInitialized);

    internal static KurrentEventData CreateEvent(BenchmarkDocument document)
        => new(Uuid.FromGuid(BenchmarkDataset.EventId(document)), KurrentConstants.EventType,
            Encoding.UTF8.GetBytes(document.Json), contentType: KurrentConstants.EventJson);

    private static KurrentEventData CreateProbeEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType,
            Encoding.UTF8.GetBytes(KurrentConstants.ProbePayload), contentType: KurrentConstants.EventJson);

    private async Task VerifyNoStreamConflictAsync(CancellationToken cancellationToken)
    {
        var stream = StreamName(KurrentConstants.SemanticsProbeSuffix);
        var firstEvent = CreateProbeEvent();
        await KurrentOwnedStreamAppend.AppendAsync(RequireWriter(), Ownership, stream, firstEvent, cancellationToken);
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

    private KurrentDBClient RequireWriter() => writer ?? throw new ComparisonFailureException(KurrentConstants.NotInitialized);
}
