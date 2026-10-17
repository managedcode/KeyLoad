using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Resolves an individual data peer through authenticated native Orleans calls.</summary>
internal sealed class ReplicaPeerDiscovery : IDisposable, IAsyncDisposable
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaDiscoveryObservationCache observations;
    private readonly ReplicaDiscoveryResourceAccess resources;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly TimeProvider clock;

    internal ReplicaPeerDiscovery(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> peerSettings,
        ReplicaSiloDiscoveryState local, ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
        ReplicaNativeDiscoveryClient native, ReplicaNativeDiscoveryMembership membership, ReplicaDiscoveryProbe? probe = null)
    {
        var configuration = configurationOptions.Value;
        var options = peerSettings.Value;
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(configuration);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(clock);
        this.configuration = configurationOptions.Value;
        this.clock = clock;
        observations = new(configurationOptions, local, clock, membership);
        var ownedResources = new ReplicaDiscoveryResources(configurationOptions, peerSettings, authentication, clock, native, membership, probe);
        resources = new(ownedResources);
        lifetime = new(ownedResources);
    }

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
