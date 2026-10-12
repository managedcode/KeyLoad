using System.Collections.Concurrent;
using System.Security.Cryptography;
using KeyLoad.Orleans;
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
    private TestCluster cluster;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    internal Guid Incarnation { get; } = Guid.NewGuid();
    internal ILocalSiloDetails LocalSilo { get; private set; } = null!;
    internal IServiceProvider LocalServices { get; private set; } = null!;
    internal IReadOnlyList<string> RuntimeAddresses { get; private set; } = [];

    internal RequestCqrsCohortRuntimeFixture()
    {
        RequestCqrsCohortOwners.Register(ownerId, this);
        try { cluster = CreateCluster(); }
        catch (Exception) { RequestCqrsCohortOwners.Release(ownerId); CryptographicOperations.ZeroMemory(secret); throw; }
    }
    private TestCluster CreateCluster()
    {
        var builder = new TestClusterBuilder(3);
        builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        builder.Properties[RequestCqrsCohortOwners.Key] = ownerId;
        builder.AddSiloBuilderConfigurator<RequestCqrsCohortNativeConfigurator>();
        return builder.Build();
    }
    internal RequestCqrsCohortNativeNode CreateNode(string voter, ILocalSiloDetails local)
    {
        if (!Voters.Contains(voter, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The native silo name is outside the configured cohort.");
        }

        var node = new RequestCqrsCohortNativeNode(voter, local,
            new(voter, Voters, Path.GetTempPath(), Incarnation)
            {
                RpcTimeout = RequestCqrsCohortScenario.RpcTimeout,
                LowerElectionTimeout = RequestCqrsCohortScenario.LowerElectionTimeout,
                UpperElectionTimeout = RequestCqrsCohortScenario.UpperElectionTimeout
            }, secret);
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
    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupBound, TimeProvider.System);
        try
        {
            await cluster.DeployAsync(deadline.Token);
            var silos = cluster.Silos.OfType<InProcessSiloHandle>().ToArray();
            if (silos.Length != Voters.Length)
            {
                throw new InvalidOperationException("The native cluster must own exactly three silos.");
            }

            LocalServices = silos.Single(s => s.ServiceProvider.GetRequiredService<ILocalSiloDetails>().Name == RequestCqrsCohortScenario.LocalVoter).ServiceProvider;
            LocalSilo = LocalServices.GetRequiredService<ILocalSiloDetails>();
            RuntimeAddresses = Voters.Select(v => Node(v).Discovery.RuntimeAddress).ToArray();
        }
        catch (Exception primary)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
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
        cluster = CreateCluster();
        await InitializeAsync();
    }
    internal async Task RestartVoterAsync(string voter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var old = Node(voter);
        var handle = cluster.Silos.OfType<InProcessSiloHandle>().Single(silo =>
            silo.ServiceProvider.GetRequiredService<ILocalSiloDetails>().Name == voter);
        var previous = old.Discovery.RuntimeAddress;
        old.Observation.ReleaseAll();
        await (old.Runtime ?? throw new InvalidOperationException("The native runtime is absent.")).StopAsync();
        await old.Journal.Consensus.StopAsync(token);
        if (!nodes.TryRemove(voter, out var removed))
        { throw new InvalidOperationException("The native voter owner changed during restart."); }
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
                { await ServerFailureObserver.ObserveAsync(() => cluster.RestartSiloAsync(handle), failures); }
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        await cluster.WaitForLivenessToStabilizeAsync();
        token.ThrowIfCancellationRequested();
        RuntimeAddresses = Voters.Select(value => Node(value).Discovery.RuntimeAddress).ToArray();
        if (Node(voter).Discovery.RuntimeAddress == previous)
        {
            throw new InvalidOperationException("The genuine native restart retained its previous generation.");
        }
    }
    private async Task StopClusterAsync()
    {
        using var deadline = new CancellationTokenSource(ShutdownBound, TimeProvider.System);
        var failures = new List<Exception>();
        foreach (var node in nodes.Values)
        {
            node.Observation.ReleaseAll();
            if (node.Runtime is { } runtime)
            {
                await ServerFailureObserver.ObserveAsync(runtime.StopAsync, failures);
            }

            await ServerFailureObserver.ObserveAsync(() => node.Journal.Consensus.StopAsync(deadline.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => cluster.StopAllSilosAsync(deadline.Token), failures);
        try
        { await cluster.DisposeAsync(); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        foreach (var node in nodes.Values)
        {
            await ServerFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures);
        }

        ServerFailureObserver.ThrowIfAny(failures);
    }
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        { return new(disposal ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        try { await StopClusterAsync(); }
        finally { RequestCqrsCohortOwners.Release(ownerId); CryptographicOperations.ZeroMemory(secret); }
    }
}
