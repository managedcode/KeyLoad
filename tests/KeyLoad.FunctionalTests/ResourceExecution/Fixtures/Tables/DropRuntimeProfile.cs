using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.ResourceExecution;

/// <summary>Owns the existing production Orleans hosts and joins original table operations before storage closes.</summary>
internal sealed class DropRuntimeProfile : IAsyncDisposable
{
    private readonly List<Task> originals = [];
    private readonly Dictionary<Task, Exception> expectedFailures = [];
    private readonly Lock lifecycle = new();
    private Task? disposal;
    private bool retain;
    internal RequestCqrsHostedCluster Hosts { get; }
    internal DropCommandPhaseObserver Observer { get; }
    internal RequestCqrsHostedRuntimeFixture Node => Hosts.Nodes[0];
    internal RequestCqrsHostedDatabase Source => Node.Database;

    private DropRuntimeProfile(RequestCqrsHostedCluster hosts, DropCommandPhaseObserver observer)
    {
        Hosts = hosts;
        Observer = observer;
    }

    internal static async Task<DropRuntimeProfile> CreateAsync(CancellationToken token, DatabaseLimits? limits = null)
    {
        var observer = new DropCommandPhaseObserver();
        var hosts = await RequestCqrsHostedCluster.CreateAsync(token, observer: observer, limits: limits).ConfigureAwait(false);
        var profile = new DropRuntimeProfile(hosts, observer);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            profile.Attach();
            _ = profile.Node.PhysicalServices.GetRequiredService<IDropTableExecution>();
            return Task.CompletedTask;
        }, failures).ConfigureAwait(false);
        if (failures.Count != DropRuntimeProtocol.NoFailures)
        {
            profile.RetainRoots();
            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return profile;
    }

    private void Attach()
    {
        foreach (var node in Hosts.Nodes)
        {
            if (!ReferenceEquals(node.Codec.PhaseObserver, Observer))
            {
                throw new InvalidOperationException(DropRuntimeProtocol.ObserverAlreadyOwned);
            }
        }
    }

    internal T Track<T>(T original) where T : Task
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            originals.Add(original);
        }
        return original;
    }

    internal void ExpectFailure(Task original, Exception failure)
    {
        lock (lifecycle)
        {
            if (!originals.Contains(original) || !expectedFailures.TryAdd(original, failure))
            {
                throw new InvalidOperationException(DropRuntimeProtocol.MissingReply);
            }
        }
    }

    internal void RetainRoots()
    {
        retain = true;
        Hosts.RetainRoots();
    }

    internal async Task RestartAsync(CancellationToken token)
    {
        await Observer.JoinSelectedAsync(token).ConfigureAwait(false);
        await Hosts.RestartAsync(token).ConfigureAwait(false);
        Attach();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        Observer.ReleaseAll();
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            var observed = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => original, observed).ConfigureAwait(false);
            foreach (var failure in observed)
            {
                if (!expectedFailures.TryGetValue(original, out var expected) || !ReferenceEquals(failure, expected))
                {
                    failures.Add(failure);
                }
            }
        }
        if (failures.Count != DropRuntimeProtocol.NoFailures || retain)
        {
            Hosts.RetainRoots();
        }
        await ServerFailureObserver.ObserveAsync(() => Hosts.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
