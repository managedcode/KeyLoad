using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Borrows two production Server silos from the shared execution Orleans fixture.</summary>
internal sealed class ConnectionServerRuntime : IAsyncDisposable
{
    internal const string NotStarted = "The actual execution Server cluster is not started.";
    private const int SourceIndex = 0;
    private const string FailureRoot = "KeyLoad.RuntimeConnection.ServerRoot";
    private readonly IGrainRequestPhaseObserver observer;
    private readonly IGrainRequestPurposeObserver? purposeObserver;
    private readonly ConnectionOperationObservation connectionObserver;
    private RequestCqrsHostedCluster? hosts;
    private string[] directories = [];
    private CancellationToken originalToken;
    private ConnectionServerInventory? inventory;
    internal Guid ConnectionId { get; } = Guid.NewGuid();
    internal IOptions<GrainRoutingOptions> RoutingOptions
    {
        get;
    }
    internal ConnectionServerEvidence Evidence { get; } = new();
    internal RuntimeTestOptions Timing { get; } = new();
    internal ConnectionServerCluster Cluster
    {
        get;
    }
    internal ConnectionServerDatabase Database
    {
        get;
    }
    internal TimeProvider Clock => Source.Clock;
    internal GrainRequestCodec Codec => Source.Codec;
    internal RequestWorkOwner RequestWork => Source.RequestWork;
    internal bool IsDisposalJoined => hosts is not null && hosts.Nodes.All(node => node.IsDisposalJoined);
    internal RequestCqrsHostedCluster Hosts => hosts
        ?? throw new InvalidOperationException(NotStarted);
    internal RequestCqrsHostedRuntimeFixture Source => Hosts.Nodes[SourceIndex];

    internal ConnectionServerRuntime(GrainRoutingOptions? routing, IGrainRequestPhaseObserver observer,
        IGrainRequestPurposeObserver? purposeObserver, ConnectionOperationObservation connectionObserver)
    {
        RoutingOptions = UnitRoutingOptions.Routing(routing);
        this.observer = observer;
        this.purposeObserver = purposeObserver;
        this.connectionObserver = connectionObserver;
        Cluster = new(() => Source);
        Database = new(() => Source, () => originalToken);
    }

    internal async Task InitializeAsync(CancellationToken token)
    {
        originalToken = token;
        using var deadline = new CancellationTokenSource(Timing.StartupTimeout, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        hosts = await RequestCqrsHostedCluster.CreateAsync(startup.Token, observer: observer,
            routing: RoutingOptions.Value, purposeObserver: purposeObserver,
            configureServices: (index, services, options) =>
            {
                Evidence.Configure(index, services, options);
                services.AddSingleton<IIncomingGrainCallFilter>(connectionObserver);
            }).ConfigureAwait(false);
        directories = hosts.Nodes.Select(node => node.Database.Directory).ToArray();
        inventory = new(hosts, Cluster.Client, ConnectionId);
    }

    internal void RegisterConsumer(Guid id) => RequireInventory().RegisterConsumer(id);
    internal Task<HashSet<GrainId>> ActiveConsumersAsync(CancellationToken token)
        => RequireInventory().ReadAsync(token);
    private ConnectionServerInventory RequireInventory() => inventory
        ?? throw new InvalidOperationException(NotStarted);

    internal void RetainRoots() => hosts?.RetainRoots();

    internal void ReportFailure(List<Exception> failures)
    {
        RetainRoots();
        ServerFailureObserver.Observe(() => Evidence.SaveOperationFailure(IsDisposalJoined, failures), failures);
        if (hosts is not null)
        {
            foreach (var failure in failures.ToArray())
            {
                ServerFailureObserver.Observe(() => failure.Data[FailureRoot] = directories, failures);
            }
        }
    }

    public ValueTask DisposeAsync() => hosts is { } actual ? actual.DisposeAsync() : ValueTask.CompletedTask;
}
