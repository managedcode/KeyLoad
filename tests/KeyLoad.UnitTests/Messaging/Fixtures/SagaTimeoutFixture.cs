using System.Diagnostics.CodeAnalysis;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SagaTimeoutFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string InvalidProfileDetail = "The execution runtime test timing profile is invalid.";
    private readonly IOptions<RuntimeTestOptions> profile = Options.Create(new RuntimeTestOptions());
    private readonly SagaTimeoutRuntimeLifecycle runtime;
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal SagaTimeoutFixture()
    {
        if (!profile.Value.IsValid())
        {
            throw new InvalidOperationException(InvalidProfileDetail);
        }
        DurableJobOptions = Options.Create(new RuntimeDurableJobOptions
        {
            ShardCheckInterval = profile.Value.PollInterval,
            JobStatusPollInterval = profile.Value.PollInterval,
            RetryDelay = profile.Value.RetryDelay
        });
        Database = new TestDatabase();
        runtime = new SagaTimeoutRuntimeLifecycle(this);
    }

    internal TestDatabase Database
    {
        get;
    }
    internal TestCluster Cluster => runtime.Cluster;
    internal GrainRequestCodec Codec
    {
        get => field ?? throw NotInitialized(nameof(Codec)); private set;
    }
    internal ICommitCoordinator Coordinator
    {
        get => field ?? throw NotInitialized(nameof(Coordinator)); private set;
    }
    internal RequestWorkOwner RequestWork => runtime.RequestWork;
    internal SagaTimeoutJobHarness JobHarness
    {
        get => field ?? throw NotInitialized(nameof(JobHarness)); private set;
    }
    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IOptions<GrainRoutingOptions> Routing { get; } = UnitRoutingOptions.Routing();
    internal IOptions<RuntimeJournalOptions> JournalOptions { get; } = Options.Create(new RuntimeJournalOptions());
    internal IOptions<RuntimeDurableJobOptions> DurableJobOptions
    {
        get;
    }
    internal IOptions<DueCoordinationOptions> DueOptions { get; } = Options.Create(new DueCoordinationOptions());
    internal RuntimeTestOptions TestProfile => profile.Value;

    [AllowNull]
    internal static SagaTimeoutFixture Current
    {
        get => field ?? throw new InvalidOperationException("The execution saga timeout fixture is not active."); private set;
    }

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TestProfile.StartupTimeout, TimeProvider.System);
        try
        {
            Database.Database.ConfigureRuntimeJournal(JournalOptions);
            Database.Configure("jobs", ResourceKind.WorkQueue);
            Database.Configure("timeouts", ResourceKind.WorkQueue);
            SagaTimeoutTestData.ConfigureSagaPrincipal(this);
            Current = this;
            await runtime.StartAsync(deadline.Token);
        }
        catch (Exception startupFailure) when (CqrsBoundaryErrors.IsNonFatal(startupFailure))
        {
            await CleanupAfterStartupFailureAsync(startupFailure);
            throw;
        }
        catch (Exception startupFailure) when (!CqrsBoundaryErrors.IsNonFatal(startupFailure))
        {
            await CleanupAfterStartupFailureAsync(startupFailure);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        await runtime.DisposeAsync();
        Current = null;
    }

    internal Task<DueDispatchResult> DispatchAsync(DueWorkHint hint, CancellationToken cancellationToken)
        => Cluster.Client.GetGrain<IRecurringDueCoordinatorGrain>(hint.Lane.Partition.AtomicPartitionId)
            .ProcessDueAsync(hint, cancellationToken);

    internal Task RestartRuntimeAsync(CancellationToken cancellationToken)
        => runtime.RestartAsync(cancellationToken);

    internal void SetRuntimeDependencies(GrainRequestCodec codec, ICommitCoordinator coordinator)
    {
        Codec = codec;
        Coordinator = coordinator;
    }

    internal void SetJobHarness(SagaTimeoutJobHarness harness) => JobHarness = harness;

    private static InvalidOperationException NotInitialized(string member)
        => new($"Execution saga timeout fixture member {member} is unavailable before initialization.");

    private async Task CleanupAfterStartupFailureAsync(Exception startupFailure)
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception cleanupFailure) when (CqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
        catch (Exception cleanupFailure) when (!CqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
    }

}
