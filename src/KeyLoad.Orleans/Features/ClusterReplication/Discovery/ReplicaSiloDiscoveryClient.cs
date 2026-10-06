using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Discovers bounded authenticated runtime addresses without using the membership table.</summary>
/// <param name="configurationOptions">The local voter set, incarnation and cache freshness bound.</param>
/// <param name="options">The fixed HTTP peer endpoints and cluster signing settings.</param>
/// <param name="local">The local actual runtime generation used for self-resolution.</param>
/// <param name="authentication">The shared request/reply and discovery verifier.</param>
/// <param name="clock">The system clock and monotonic cache timer.</param>
/// <param name="peerOptions">The centrally validated discovery timing and replay limits.</param>
public sealed class ReplicaSiloDiscoveryClient(IOptions<ReplicaConfiguration> configurationOptions,
    IOptions<ReplicaPeerOptions> options, ReplicaSiloDiscoveryState local,
    ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
    IOptions<PeerDiscoveryOptions> peerOptions) : IDisposable, IAsyncDisposable
{
    private readonly ReplicaCohortDiscovery cohort = new(
        configurationOptions ?? throw new ArgumentNullException(nameof(configurationOptions)),
        options ?? throw new ArgumentNullException(nameof(options)),
        local ?? throw new ArgumentNullException(nameof(local)),
        authentication ?? throw new ArgumentNullException(nameof(authentication)),
        clock ?? throw new ArgumentNullException(nameof(clock)),
        peerOptions ?? throw new ArgumentNullException(nameof(peerOptions)));

    /// <summary>Returns a current compatible silo generation, refreshing once under the caller's bounded deadline.</summary>
    /// <param name="voterId">The configured voter to resolve.</param>
    /// <param name="refresh">Whether to bypass a still-fresh cache entry.</param>
    /// <param name="cancellationToken">Cancellation for the wait, HTTP request and bounded response read.</param>
    /// <returns>The current Orleans silo address including its generation.</returns>
    public Task<SiloAddress> ResolveAsync(string voterId, bool refresh, CancellationToken cancellationToken)
        => cohort.ResolveAsync(voterId, refresh, cancellationToken);

    /// <summary>Authenticates the fixed voter cohort before public request admission.</summary>
    /// <param name="cancellationToken">The outer request or readiness deadline.</param>
    public Task EnsureCompatibleCohortAsync(CancellationToken cancellationToken)
        => cohort.EnsureCompatibleCohortAsync(cancellationToken);

    internal Task<bool> HasNativeJournalCohortAsync(CancellationToken cancellationToken)
        => cohort.HasNativeJournalCohortAsync(cancellationToken);

    /// <summary>Whether fresh authenticated observations prove the compatible RF3 majority.</summary>
    public bool HasCompatibleCohort => cohort.HasCompatibleCohort;

    /// <inheritdoc />
    public void Dispose() => cohort.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => cohort.DisposeAsync();
}
