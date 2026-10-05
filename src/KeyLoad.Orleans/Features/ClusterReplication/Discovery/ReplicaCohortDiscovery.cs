using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Owns fixed-voter compatibility observations and bounded sequential admission.</summary>
internal sealed class ReplicaCohortDiscovery : IDisposable, IAsyncDisposable
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaDiscoveryObservationCache observations;
    private readonly ReplicaDiscoveryResourceAccess resources;
    private readonly ReplicaCohortAdmission admission;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly TimeProvider clock;
    private readonly IReplicaDiscoveryObservationSink? observationSink;

    internal ReplicaCohortDiscovery(IOptions<ReplicaConfiguration> configurationOptions, ReplicaPeerOptions options,
        ReplicaSiloDiscoveryState local, ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
        IOptions<PeerDiscoveryOptions> peerOptions, IReplicaDiscoveryObservationSink? observationSink = null)
    {
        var configuration = configurationOptions.Value;
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(configuration);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(clock);
        this.configuration = configuration;
        this.clock = clock;
        this.observationSink = observationSink;
        observations = new(configuration, local, clock);
        var ownedResources = new ReplicaDiscoveryResources(configurationOptions, options, authentication, clock, peerOptions);
        resources = new(ownedResources);
        admission = new(configuration, observations, GetObservationAsync);
        lifetime = new(ownedResources);
    }

    internal bool HasCompatibleCohort => !lifetime.IsStopping && admission.HasCompatibleCohort;

    internal async Task<SiloAddress> ResolveAsync(string voterId, bool refresh, CancellationToken cancellationToken)
    {
        if (!configuration.VoterIds.Contains(voterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaProtocol.InvalidPeer);
        }

        using var operation = EnterOperation();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.ShutdownToken);
        request.Token.ThrowIfCancellationRequested();
        if (voterId == configuration.LocalId)
        {
            var localAddress = observations.ResolveLocal();
            request.Token.ThrowIfCancellationRequested();
            return localAddress;
        }

        var observation = await GetObservationAsync(voterId, refresh, request.Token).ConfigureAwait(false);
        request.Token.ThrowIfCancellationRequested();
        if (observation is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var address = ReplicaDiscoveryObservationCache.RequireCompatible(observation);
        request.Token.ThrowIfCancellationRequested();
        return address;
    }

    internal async Task EnsureCompatibleCohortAsync(CancellationToken cancellationToken)
    {
        using var operation = EnterOperation();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.ShutdownToken);
        request.Token.ThrowIfCancellationRequested();
        await admission.EnsureCompatibleCohortAsync(request.Token).ConfigureAwait(false);
    }

    private ReplicaDiscoveryLifetime.Operation EnterOperation()
        => lifetime.TryEnter() ?? throw new ObjectDisposedException(nameof(ReplicaSiloDiscoveryClient));

    private async Task<ReplicaDiscoveryObservation?> GetObservationAsync(string voterId, bool refresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!refresh && observations.TryFresh(voterId, out var cached))
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (refresh)
        {
            observations.Remove(voterId);
        }

        using var timeout = new CancellationTokenSource(configuration.RpcTimeout, clock);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await resources.WaitForDiscoveryAsync(attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            if (!refresh && observations.TryFresh(voterId, out cached))
            {
                return cached;
            }

            observations.Remove(voterId);
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = await DiscoverAndObserveAsync(voterId, attempt.Token, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (discovered is not null)
            {
                observations.Store(voterId, discovered);
            }

            return discovered;
        }
        finally
        {
            resources.ReleaseDiscoveryGate();
        }
    }

    private async Task<ReplicaDiscoveryObservation?> DiscoverAndObserveAsync(string voterId,
        CancellationToken attemptToken, CancellationToken cancellationToken)
    {
        ReplicaDiscoveryObservation? discovered;
        try
        {
            discovered = await resources.DiscoverAsync(voterId, attemptToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested
            && (attemptToken.IsCancellationRequested || error is TaskCanceledException))
        {
            return null;
        }
        if (discovered is { ProtocolCompatible: false } && observationSink is not null)
        {
            await observationSink.ObserveIncompatibleAsync(voterId, discovered, attemptToken)
                .ConfigureAwait(false);
        }
        return discovered;
    }

    public void Dispose()
    {
        var shutdown = lifetime.StopAsync();
        if (shutdown.IsCompleted)
        {
            shutdown.GetAwaiter().GetResult();
        }
    }

    public ValueTask DisposeAsync() => lifetime.DisposeAsync();
}
