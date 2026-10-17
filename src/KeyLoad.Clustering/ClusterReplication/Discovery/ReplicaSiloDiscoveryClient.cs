using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Resolves bounded authenticated runtime addresses from actual active native silo membership.</summary>
public sealed class ReplicaSiloDiscoveryClient : IDisposable, IAsyncDisposable
{
    private readonly ReplicaCohortDiscovery cohort;

    /// <summary>Retains the original configured discovery owner and native authentication lifetime.</summary>
    /// <param name="configurationOptions">The local voter set, incarnation and cache freshness bound.</param>
    /// <param name="options">The fixed cluster signing settings.</param>
    /// <param name="local">The local actual runtime generation used for self-resolution.</param>
    /// <param name="authentication">The shared request/reply and discovery verifier.</param>
    /// <param name="clock">The system clock and monotonic cache timer.</param>
    /// <param name="services">The actual silo service provider.</param>
    /// <param name="oracle">The actual active native silo status and name oracle.</param>
    /// <param name="membership">The actual configured native membership inventory bound.</param>
    public ReplicaSiloDiscoveryClient(IOptions<ReplicaConfiguration> configurationOptions,
        IOptions<ReplicaPeerOptions> options, ReplicaSiloDiscoveryState local,
        ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
        IServiceProvider services, ISiloStatusOracle oracle, IOptions<OrleansMembershipOptions> membership)
        : this(configurationOptions, options, local, authentication, clock, services, oracle, membership, null) { }

    internal ReplicaSiloDiscoveryClient(IOptions<ReplicaConfiguration> configurationOptions,
        IOptions<ReplicaPeerOptions> options, ReplicaSiloDiscoveryState local,
        ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
        IServiceProvider services, ISiloStatusOracle oracle, IOptions<OrleansMembershipOptions> membership,
        ReplicaDiscoveryProbe? probe)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentNullException.ThrowIfNull(membership);
        cohort = new(configurationOptions ?? throw new ArgumentNullException(nameof(configurationOptions)),
            options ?? throw new ArgumentNullException(nameof(options)),
            local ?? throw new ArgumentNullException(nameof(local)),
            authentication ?? throw new ArgumentNullException(nameof(authentication)),
            clock ?? throw new ArgumentNullException(nameof(clock)),
            new ReplicaNativeDiscoveryClient(services), new ReplicaNativeDiscoveryMembership(oracle, membership), probe);
    }

    /// <summary>Returns a current compatible silo generation, refreshing once under the caller's bounded deadline.</summary>
    /// <param name="voterId">The configured voter to resolve.</param>
    /// <param name="refresh">Whether to bypass a still-fresh cache entry.</param>
    /// <param name="cancellationToken">Cancellation for the original joined native discovery RPC.</param>
    /// <returns>The current Orleans silo address including its generation.</returns>
    public Task<SiloAddress> ResolveAsync(string voterId, bool refresh, CancellationToken cancellationToken)
        => cohort.ResolveAsync(voterId, refresh, cancellationToken);

    /// <summary>Authenticates the fixed voter cohort before public request admission.</summary>
    /// <param name="cancellationToken">The outer request or readiness deadline.</param>
    public Task EnsureCompatibleCohortAsync(CancellationToken cancellationToken)
        => cohort.EnsureCompatibleCohortAsync(cancellationToken);

    internal Task<bool> HasNativeJournalCohortAsync(CancellationToken cancellationToken)
        => cohort.HasNativeJournalCohortAsync(cancellationToken);

    /// <summary>Whether fresh authenticated observations prove the compatible replicated majority.</summary>
    public bool HasCompatibleCohort => cohort.HasCompatibleCohort;

    /// <summary>Observes the same fixed-voter predicate without refreshing or authorizing discovery.</summary>
    /// <returns>Native-local closed status and counts; it is never readiness authority.</returns>
    public ReplicaCohortAdmissionStatus ObserveCompatibleCohort() => cohort.ObserveCompatibleCohort();

    internal Task<ReplicaCohortAdmissionStatus> AcquireCompatibleCohortAsync(CancellationToken token)
        => ReplicaCohortStartupAcquisition.AcquireAsync(this, token);

    /// <inheritdoc />
    public void Dispose() => cohort.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => cohort.DisposeAsync();
}
