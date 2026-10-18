using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Borrows the shared production Orleans cluster and its original native options.</summary>
internal sealed class CommandObservedPolicyRuntime : IAsyncDisposable
{
    private const int SourceIndex = 0;
    private readonly RequestCqrsHostedCluster hosts;
    private readonly ConnectionOperationObservation observation;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    internal RequestCqrsHostedRuntimeFixture Source => hosts.Nodes[SourceIndex];
    internal CommandObservedPolicyDatabase Database
    {
        get;
    }
    internal NativeRuntimeTestOptions Timing { get; } = new();

    private CommandObservedPolicyRuntime(RequestCqrsHostedCluster hosts,
        ConnectionOperationObservation observation)
    {
        this.hosts = hosts;
        this.observation = observation;
        Database = new(() => Source);
    }

    internal static async Task<CommandObservedPolicyRuntime> CreateAsync(ConnectionOperationObservation observation,
        CancellationToken token)
    {
        var hosts = await RequestCqrsHostedCluster.CreateAsync(token, observer: observation,
            configureServices: (_, services, _) => services.AddSingleton<IIncomingGrainCallFilter>(observation))
            .ConfigureAwait(false);
        var profile = new CommandObservedPolicyRuntime(hosts, observation);
        try
        {
            profile.Attach();
            return profile;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            hosts.RetainRoots();
            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void Attach()
    {
        foreach (var node in hosts.Nodes)
        {
            var codec = node.Codec;
            if (!ReferenceEquals(codec.PhaseObserver, observation))
            {
                throw new InvalidOperationException(P.ObserverOwned);
            }
        }
    }

    internal void RetainRoots() => hosts.RetainRoots();

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        observation.ReleaseAll();
        await hosts.DisposeAsync().ConfigureAwait(false);
    }
}
