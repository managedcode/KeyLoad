using System.Collections.Concurrent;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;
namespace KeyLoad.UnitTests.Features.ClusterReplication;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsCohortDataSourceAttribute
    : DataSourceGeneratorAttribute<RequestCqrsCohortRuntimeFixture>
{
    protected override IEnumerable<Func<RequestCqrsCohortRuntimeFixture>> GenerateDataSources(
        DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsCohortRuntimeFixture>(
            SharedType.PerTestSession,
            metadata,
            null,
            static () => new RequestCqrsCohortRuntimeFixture());
    }
}

internal sealed class RequestCqrsCohortRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    internal static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);
    private readonly string ownerId = Guid.NewGuid().ToString("N");
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
    private readonly ConcurrentDictionary<string, RequestCqrsCohortNativeNode> nodes = new(StringComparer.Ordinal);
    private readonly RequestCqrsCohortCheckpointScenario? checkpoints;
    private readonly ReplicaBarrierDeadlineScenario? barrierDeadline;
    private readonly byte[]? canonicalKey;
    internal global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedProfile? Hosted
    {
        get;
    }
    internal TestCluster Cluster
    {
        get; private set;
    }
    internal bool HasBarrierDeadline => barrierDeadline is not null;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    internal Guid Incarnation { get; } = Guid.NewGuid();
    internal ILocalSiloDetails LocalSilo { get; private set; } = null!;
    internal IServiceProvider LocalServices { get; private set; } = null!;
    internal IReadOnlyList<string> RuntimeAddresses { get; private set; } = [];

    internal RequestCqrsCohortRuntimeFixture(RequestCqrsCohortCheckpointScenario? checkpoints = null,
        ReplicaBarrierDeadlineScenario? barrierDeadline = null,
        global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedProfile? hosted = null)
    {
        if (checkpoints is not null && barrierDeadline is not null)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new ArgumentException("Native checkpoint and barrier observers require separate fixture ownership.");
        }
        if (hosted is not null && (checkpoints is not null || barrierDeadline is not null))
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new ArgumentException(global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedProtocol.ProfileRequired);
        }
        Hosted = hosted;
        this.checkpoints = checkpoints;
        this.barrierDeadline = barrierDeadline;
        canonicalKey = barrierDeadline is null && hosted is null ? null : RandomNumberGenerator.GetBytes(ReplicaBarrierDeadlineScenario.CanonicalKeyBytes);
        try
        {
            RequestCqrsCohortOwners.Register(ownerId, this);
            Cluster = CreateCluster();
        }
        catch (Exception)
        {
            ReleaseIdentity();
            throw;
        }
    }
    private TestCluster CreateCluster() => RequestCqrsCohortClusterFactory.Create(ownerId, Hosted is not null);
    internal RequestCqrsCohortNativeNode CreateNode(string voter, ILocalSiloDetails local, IServiceProvider services)
    {
        if (!Voters.Contains(voter, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The native silo name is outside the configured cohort.");
        }

        var node = new RequestCqrsCohortNativeNode(voter, local, services,
            ReplicaBarrierDeadlineScenario.Configuration(voter, Voters, Incarnation, barrierDeadline is not null),
            secret, checkpoints, barrierDeadline,
            canonicalKey is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(canonicalKey), Hosted);
        if (nodes.TryAdd(voter, node))
        {
            return node;
        }

        node.DisposeAsync().AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException("Duplicate native cohort silo owner.");
    }
    private static readonly System.Collections.Immutable.ImmutableArray<string> Voters =
        [RequestCqrsCohortScenario.LocalVoter, RequestCqrsCohortScenario.FirstRemote, RequestCqrsCohortScenario.SecondRemote];
    internal RequestCqrsCohortNativeNode Node(string voter) => nodes[voter];
    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);
    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(StartupBound, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            await Cluster.DeployAsync(startup.Token);
            var silos = Cluster.Silos.OfType<InProcessSiloHandle>().ToArray();
            if (silos.Length != Voters.Length)
            {
                throw new InvalidOperationException("The native cluster must own exactly three silos.");
            }

            LocalServices = silos.Single(s => s.ServiceProvider.GetRequiredService<ILocalSiloDetails>().Name == RequestCqrsCohortScenario.LocalVoter).ServiceProvider;
            LocalSilo = LocalServices.GetRequiredService<ILocalSiloDetails>();
            RuntimeAddresses = Voters.Select(v => Node(v).Discovery.RuntimeAddress).ToArray();
            Hosted?.BindLocalServices(LocalServices);
        }
        catch (Exception primary)
        {
            checkpoints?.Release();
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
    internal string RuntimeAddress(int index) => RuntimeAddresses[index];
    internal async Task RestartIfStoppedAsync()
    {
        if (!nodes.Values.Any(n => n.Observation.Stopped))
        {
            return;
        }

        await StopClusterAsync();
        nodes.Clear();
        Cluster = CreateCluster();
        await InitializeAsync();
    }
    internal async Task RestartVoterAsync(string voter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var old = Node(voter);
        var handle = Cluster.Silos.OfType<InProcessSiloHandle>().Single(silo =>
            silo.ServiceProvider.GetRequiredService<ILocalSiloDetails>().Name == voter);
        var previous = old.Discovery.RuntimeAddress;
        old.Observation.ReleaseAll();
        await (old.Runtime ?? throw new InvalidOperationException("The native runtime is absent.")).StopAsync();
        await old.Journal.Consensus.StopAsync(token);
        if (!nodes.TryRemove(voter, out var removed))
        {
            throw new InvalidOperationException("The native voter owner changed during restart.");
        }
        var failures = new List<Exception>();
        try
        {
            await using (removed)
            {
                if (!ReferenceEquals(old, removed))
                {
                    failures.Add(new InvalidOperationException("The native voter owner changed during restart."));
                    await ServerFailureObserver.ObserveAsync(() => old.DisposeAsync().AsTask(), failures);
                }
                else
                {
                    await ServerFailureObserver.ObserveAsync(() => Cluster.RestartSiloAsync(handle), failures);
                }
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        await Cluster.WaitForLivenessToStabilizeAsync();
        token.ThrowIfCancellationRequested();
        RuntimeAddresses = Voters.Select(value => Node(value).Discovery.RuntimeAddress).ToArray();
        if (Node(voter).Discovery.RuntimeAddress == previous)
        {
            throw new InvalidOperationException("The genuine native restart retained its previous generation.");
        }
    }
    private Task StopClusterAsync()
        => RequestCqrsCohortShutdown.StopAsync(Cluster, nodes.Values, () =>
        {
            checkpoints?.Release();
            barrierDeadline?.Release();
        }, ShutdownBound);

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private void ReleaseIdentity() => RequestCqrsCohortIdentity.Release(ownerId, secret, canonicalKey);

    private async Task DisposeCoreAsync()
    {
        var joined = false;
        try
        {
            await StopClusterAsync();
            joined = true;
        }
        finally
        {
            if (Hosted is null || joined)
            {
                ReleaseIdentity();
            }
        }
    }
}
