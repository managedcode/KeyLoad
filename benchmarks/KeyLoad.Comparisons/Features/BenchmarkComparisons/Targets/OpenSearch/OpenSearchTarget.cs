using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Runs document CRUD and exact-vector comparisons against an isolated OpenSearch index.</summary>
/// <param name="client">HTTP client configured for the OpenSearch endpoint and Aspire credentials; the target disposes it.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate the index name.</param>
/// <param name="image">Pinned OpenSearch image reference checked during initialization and recorded in the profile.</param>
/// <param name="topology">The expected one, two or three native nodes verified against the created index.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="nativeExecutionOptions">Centrally validated native adapter execution policy.</param>
public sealed class OpenSearchTarget(HttpClient client, string runId, string image, ComparisonTopology topology,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions) : IComparisonTarget
{
    private readonly IOptions<NativeComparisonExecutionOptions> executionOptions = NativeComparisonExecutionOptions.Require(nativeExecutionOptions);

    private readonly string index = OpenSearchNames.IndexNamePrefix + Guid.Parse(runId).ToString(OpenSearchNames.GuidFormat);
    private readonly int expectedCopies = ComparisonTopologies.NodeCount(topology);
    private bool indexCreated;
    private int topK;
    private int corpusCount;

    /// <summary>Gets the observed server, transport, index-copy, and acknowledgement evidence collected during initialization.</summary>
    public TargetProfile Profile { get; private set; } = new(OpenSearchNames.TargetName, OpenSearchNames.Unverified,
        topology == ComparisonTopology.Standalone ? OpenSearchNames.SingleTopology : topology == ComparisonTopology.TwoNode ? OpenSearchNames.TwoNodeTopology : OpenSearchNames.ReplicatedTopology,
        topology == ComparisonTopology.Standalone ? OpenSearchNames.SingleAcknowledgement : topology == ComparisonTopology.TwoNode ? OpenSearchNames.TwoNodeAcknowledgement : OpenSearchNames.ReplicatedAcknowledgement,
        OpenSearchNames.RealtimeReadContract, OpenSearchNames.HttpJson, OpenSearchNames.AspireAuthorization, image);

    /// <summary>Gets the limitation text for scenarios that this target does not implement.</summary>
    public string UnsupportedReason => OpenSearchNames.Unsupported;
    /// <summary>Reports support for document CRUD and exact vector search.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a supported scenario; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.VectorExact;

    /// <summary>Checks the pinned server version, creates and seeds the index, verifies replica copies and settings, then records evidence.</summary>
    /// <param name="dataset">The deterministic document and vector corpus and query options.</param>
    /// <param name="cancellationToken">A token that cancels HTTP requests and index setup.</param>
    /// <returns>A task that completes after index and cluster evidence have been collected.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        const string RealTimeDocumentGETS1SourceContainsNoVectorValuesToken = "real-time document GET; S1 source contains no vector values";
        const int AdjacentElementOffset = 1;
        const int FirstElementIndex = 0;

        ArgumentNullException.ThrowIfNull(dataset);
        if (image != OpenSearchNames.ExpectedImage)
        {
            throw new ComparisonFailureException(OpenSearchNames.PinnedImageMismatch);
        }

        topK = dataset.Settings.TopK;
        if (dataset.Settings is ScaledComparisonProfile)
        {
            Profile = Profile with { ReadContract = RealTimeDocumentGETS1SourceContainsNoVectorValuesToken };
        }
        corpusCount = dataset.Documents.Count;
        using var root = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get, OpenSearchNames.PathSeparator, null, cancellationToken);
        var version = OpenSearchJson.RequiredString(root.RootElement, OpenSearchNames.Version, OpenSearchNames.VersionNumber);
        if (version != OpenSearchNames.ExpectedVersion)
        {
            throw new ComparisonFailureException(OpenSearchNames.ServerVersionMismatch);
        }

        await OpenSearchIndex.CreateAsync(client, index, dataset.Settings.Dimensions, expectedCopies - AdjacentElementOffset, cancellationToken);
        indexCreated = true;
        var beforeSeed = await OpenSearchClusterEvidence.ObserveAsync(client, index, expectedCopies, topology, cancellationToken);
        await OpenSearchIndex.SeedAsync(client, index, dataset.Documents, expectedCopies, executionOptions, cancellationToken);
        if (dataset.Settings is not ScaledComparisonProfile)
        {
            await OpenSearchProbe.VerifyAsync(client, index, dataset.Documents[FirstElementIndex].Vector, expectedCopies, cancellationToken);
        }
        using (var refresh = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.RefreshSuffix, null, cancellationToken))
        {
            _ = refresh.RootElement;
        }
        var settings = await OpenSearchIndex.VerifySettingsAsync(client, index, expectedCopies - AdjacentElementOffset, cancellationToken);
        var afterSeed = await OpenSearchClusterEvidence.ObserveAsync(client, index, expectedCopies, topology, cancellationToken);
        Profile = Profile with
        {
            Version = version,
            Transport = client.BaseAddress?.Scheme == Uri.UriSchemeHttps ? OpenSearchNames.TlsTransport : OpenSearchNames.TcpTransport,
            Cluster = afterSeed.ToClusterEvidence(version, beforeSeed, settings, expectedCopies)
        };
    }

    /// <summary>Opens a session that uses the seeded index for the supported comparison operations.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; session creation itself does not perform I/O.</param>
    /// <returns>A comparison session backed by this target’s HTTP client and index.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new OpenSearchSession(client, index, topK, expectedCopies, corpusCount, executionOptions));

    /// <summary>Deletes the run-specific index when created and disposes the target-owned HTTP client.</summary>
    /// <returns>A value task that completes after index cleanup and client disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (indexCreated)
            {
                using var timeout = new CancellationTokenSource(lifecycleOptions.Value.OpenSearchCleanupTimeout);
                using var response = await client.DeleteAsync(
                    new Uri(OpenSearchNames.PathSeparator + index + OpenSearchNames.DeleteIndexSuffix, UriKind.RelativeOrAbsolute), timeout.Token);
                response.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            client.Dispose();
        }
    }
}
