using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class HttpDocumentTarget(HttpClient http, HttpDocumentStore store, string image,
    IOptions<ComparisonExecutionOptions> options) : IDocumentComparisonTarget, IBenchmarkSessionQueries
{
    private const string Unobserved = "unobserved", Topology = "one execution node; no replicas";
    private const string Transport = "HTTP; bounded responses; independent client connections";
    private const string ReadContract = "acknowledged writes are queryable; complete document readback";
    private const string InfluxLimit = "InfluxDB 3 Core has no row DELETE API; the mixed CRUD contract is unavailable.";
    private const string OutsideContract = "Outside this target's shared document contract.";
    private const int SingleCopy = 1;
    private bool initialized;
    private const string AlreadyInitialized = "HttpDocumentTargetAlreadyInitialized";
    public TargetProfile Profile { get; private set; } = new(store.Target, Unobserved, Topology,
        store.Acknowledgement, ReadContract, Transport, store.Authorization, image);
    public string UnsupportedReason => store.Target == BenchmarkAdditionalTargets.InfluxDb ? InfluxLimit : OutsideContract;
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite
        or Scenario.DocumentUpdate || scenario == Scenario.DocumentDelete && store.SupportsDelete;

    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (initialized)
        {
            throw new InvalidOperationException(AlreadyInitialized);
        }
        initialized = true;
        var version = await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Profile = Profile with { Version = version, Cluster = new(SingleCopy, SingleCopy, Topology, [store.Acknowledgement]) };
        foreach (var document in dataset.Documents)
        {
            await store.WriteAsync(Scenario.DocumentWrite, document, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken cancellationToken)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, cancellationToken);
    }

    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => await OpenDocumentSessionAsync(cancellationToken).ConfigureAwait(false);

    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken cancellationToken)
        => DocumentHttpSessionAcquisition.OpenAsync(http, options,
            (client, token) => store.ForClient(client).AdmitAsync(token),
            client => new HttpDocumentSession(store.ForClient(client)), cancellationToken);

    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken) => store.IndexAsync(cancellationToken);

    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        using var result = await store.ReadAsync(expected.Id, expected.Number, cancellationToken).ConfigureAwait(false);
        BenchmarkSessionQueryContract.Require(HttpDocumentSession.One(HttpDocumentStore.Documents(result)), expected);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (store.OwnsNamespace)
            {
                using var deadline = new CancellationTokenSource(options.Value.CleanupTimeout);
                await store.DropAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }
}
