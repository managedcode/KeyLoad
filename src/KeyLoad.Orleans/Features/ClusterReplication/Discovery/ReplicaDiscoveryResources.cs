using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Provides bounded operations which borrow the discovery resource owner's exchange and gate.</summary>
internal sealed class ReplicaDiscoveryResourceAccess(ReplicaDiscoveryResources owner)
{
    internal Task<ReplicaDiscoveryObservation?> DiscoverAsync(string voterId, CancellationToken cancellationToken)
        => owner.DiscoverAsync(voterId, cancellationToken);

    internal Task WaitForDiscoveryAsync(CancellationToken cancellationToken)
        => owner.WaitForDiscoveryAsync(cancellationToken);

    internal void ReleaseDiscoveryGate() => owner.ReleaseDiscoveryGate();
}

/// <summary>Owns the exchange, gate and shutdown source until discovery lifetime settlement.</summary>
internal sealed class ReplicaDiscoveryResources : IDisposable
{
    private readonly ReplicaDiscoveryExchange exchange;
    private readonly SemaphoreSlim discoveryGate;
    private readonly CancellationTokenSource stopping;
    private int disposed;

    internal ReplicaDiscoveryResources(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> options,
        ReplicaEnvelopeAuthenticator authentication, TimeProvider clock, IOptions<PeerDiscoveryOptions> peerOptions)
    {
        exchange = new(configurationOptions, options, authentication, clock, peerOptions);
        discoveryGate = new(1, 1);
        stopping = new();
    }

    internal CancellationTokenSource StoppingSource => stopping;

    public Task<ReplicaDiscoveryObservation?> DiscoverAsync(string voterId, CancellationToken cancellationToken)
        => exchange.DiscoverAsync(voterId, cancellationToken);

    public Task WaitForDiscoveryAsync(CancellationToken cancellationToken)
        => discoveryGate.WaitAsync(cancellationToken);

    public void ReleaseDiscoveryGate() => discoveryGate.Release();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>(3);
        DisposeExchange(failures);
        DisposeDiscoveryGate(failures);
        DisposeStoppingSource(failures);
        ReplicaDiscoveryLifetime.ThrowIfAny(failures);
    }

    private void DisposeExchange(List<Exception> failures)
    {
        try
        {
            exchange.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private void DisposeDiscoveryGate(List<Exception> failures)
    {
        try
        {
            discoveryGate.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private void DisposeStoppingSource(List<Exception> failures)
    {
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }
}
