using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;
namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeCluster : IAsyncInitializer, IAsyncDisposable
{
    internal const string SenderVoter = "Primary";
    internal const string ReceiverVoter = "Secondary_1";
    internal const string ThirdVoter = "Secondary_2";
    internal static readonly ImmutableArray<string> Voters = [SenderVoter, ReceiverVoter, ThirdVoter];
    internal static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
    private readonly ConcurrentDictionary<string, PeerDiscoveryNativeNode> nodes = new(StringComparer.Ordinal);
    private readonly TestCluster cluster;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    private Task? coldClose;
    private readonly PeerDiscoveryNativeClockProfile? clockProfile;
    internal IReadOnlyCollection<PeerDiscoveryNativeNode> Nodes => nodes.Values.ToArray();
    private Guid Incarnation { get; } = Guid.NewGuid();
    internal IServiceProvider SenderServices { get; private set; } = null!;
    internal IServiceProvider ReceiverServices { get; private set; } = null!;
    internal PeerDiscoveryNativeNode Sender => nodes[SenderVoter];
    internal PeerDiscoveryNativeNode Receiver => nodes[ReceiverVoter];

    internal PeerDiscoveryNativeCluster(PeerDiscoveryNativeClockProfile? profile = null)
    {
        clockProfile = profile;
        PeerDiscoveryNativeOwners.Register(id, this);
        try
        {
            var builder = new TestClusterBuilder((short)Voters.Length);
            builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            builder.Properties[PeerDiscoveryNativeOwners.Key] = id;
            builder.AddSiloBuilderConfigurator<PeerDiscoveryNativeConfigurator>();
            cluster = builder.Build();
        }
        catch (Exception)
        {
            PeerDiscoveryNativeOwners.Release(id);
            CryptographicOperations.ZeroMemory(secret);
            throw;
        }
    }

    internal PeerDiscoveryNativeNode CreateNode(string voter, ILocalSiloDetails local)
    {
        if (!Voters.Contains(voter, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The real silo name is outside the configured recovery cohort.");
        }
        var node = new PeerDiscoveryNativeNode(new(voter, Voters, Path.GetTempPath(), Incarnation), local, secret, clockProfile);
        if (nodes.TryAdd(voter, node))
        {
            return node;
        }
        node.DisposeAsync().AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException("Duplicate recovery native silo.");
    }

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupBound, TimeProvider.System);
        try
        {
            await cluster.DeployAsync(deadline.Token);
            var silos = cluster.Silos.OfType<InProcessSiloHandle>().ToArray();
            if (silos.Length != Voters.Length)
            {
                throw new InvalidOperationException("Exactly three real silos are required.");
            }
            SenderServices = FindServices(silos, SenderVoter);
            ReceiverServices = FindServices(silos, ReceiverVoter);
            var entries = SenderServices.GetRequiredService<ReplicaNativeDiscoveryRuntime>().ReadActiveMembership();
            if (!entries.Select(row => row.SiloName).SequenceEqual(Voters.Order(StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Actual native membership differs from the configured cohort.");
            }
        }
        catch (Exception primary)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    private static IServiceProvider FindServices(IEnumerable<InProcessSiloHandle> silos, string voter)
        => silos.Single(silo => silo.ServiceProvider.GetRequiredService<ILocalSiloDetails>().Name == voter).ServiceProvider;

    internal Task CloseForColdAsync()
    {
        lock (lifecycle)
        {
            if (disposal is not null)
            {
                throw new InvalidOperationException(PeerDiscoveryNativeClockProfile.ColdBeforeJoined);
            }
            return coldClose ??= CloseForColdCoreAsync();
        }
    }

    private async Task CloseForColdCoreAsync()
    {
        foreach (var node in nodes.Values)
        {
            node.RetainDirectoryForCold();
        }
        using var deadline = new CancellationTokenSource(ShutdownBound, TimeProvider.System);
        List<Exception> failures = [];
        await PeerDiscoveryNativeColdShutdown.StopAllAsync(nodes.Values, failures, deadline.Token);
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => cluster.StopAllSilosAsync(deadline.Token), failures);
        try
        {
            await cluster.DisposeAsync();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
        foreach (var node in nodes.Values)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(node.CloseForColdAsync, failures);
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    internal void ReopenAllAfterCold()
    {
        if (coldClose is not { IsCompletedSuccessfully: true })
        {
            throw new InvalidOperationException(PeerDiscoveryNativeClockProfile.ColdBeforeJoined);
        }
        foreach (var node in nodes.Values)
        {
            node.ReopenAfterJoinedCold();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        using var deadline = new CancellationTokenSource(ShutdownBound, TimeProvider.System);
        List<Exception> failures = [];
        if (coldClose is not null)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => coldClose, failures);
        }
        else
        {
            await PeerDiscoveryNativeColdShutdown.StopAllAsync(nodes.Values, failures, deadline.Token);
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => cluster.StopAllSilosAsync(deadline.Token), failures);
            try
            {
                await cluster.DisposeAsync();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
        }
        foreach (var node in nodes.Values)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(node.DisposeColdAndDeleteAsync, failures);
        }
        PeerDiscoveryNativeOwners.Release(id);
        CryptographicOperations.ZeroMemory(secret);
        clockProfile?.Dispose();
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
