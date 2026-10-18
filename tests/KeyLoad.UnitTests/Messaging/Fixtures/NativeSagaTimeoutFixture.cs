using System.Diagnostics.CodeAnalysis;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string InvalidProfileDetail = "The native runtime test timing profile is invalid.";
    private readonly IOptions<NativeRuntimeTestOptions> profile = Options.Create(new NativeRuntimeTestOptions());
    private readonly NativeSagaTimeoutRuntimeLifecycle runtime;
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal NativeSagaTimeoutFixture()
    {
        if (!profile.Value.IsValid())
        {
            throw new InvalidOperationException(InvalidProfileDetail);
        }
        DurableJobOptions = Options.Create(new NativeDurableJobOptions
        {
            ShardCheckInterval = profile.Value.PollInterval,
            JobStatusPollInterval = profile.Value.PollInterval,
            RetryDelay = profile.Value.RetryDelay
        });
        Database = new TestDatabase();
        runtime = new NativeSagaTimeoutRuntimeLifecycle(this);
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
    internal NativeRequestWorkOwner RequestWork => runtime.RequestWork;
    internal NativeSagaTimeoutJobHarness JobHarness
    {
        get => field ?? throw NotInitialized(nameof(JobHarness)); private set;
    }
    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IOptions<GrainRoutingOptions> Routing { get; } = UnitRoutingOptions.Routing();
    internal IOptions<RuntimeJournalOptions> JournalOptions { get; } = Options.Create(new RuntimeJournalOptions());
    internal IOptions<NativeDurableJobOptions> DurableJobOptions
    {
        get;
    }
    internal IOptions<DueCoordinationOptions> DueOptions { get; } = Options.Create(new DueCoordinationOptions());
    internal NativeRuntimeTestOptions TestProfile => profile.Value;

    [AllowNull]
    internal static NativeSagaTimeoutFixture Current
    {
        get => field ?? throw new InvalidOperationException("The native saga timeout fixture is not active."); private set;
    }

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TestProfile.StartupTimeout, TimeProvider.System);
        try
        {
            Database.Database.ConfigureRuntimeJournal(JournalOptions);
            Database.Configure("jobs", ResourceKind.WorkQueue);
            Database.Configure("timeouts", ResourceKind.WorkQueue);
            NativeSagaTimeoutTestData.ConfigureSagaPrincipal(this);
            Current = this;
            await runtime.StartAsync(deadline.Token);
        }
        catch (Exception startupFailure) when (NativeCqrsBoundaryErrors.IsNonFatal(startupFailure))
        {
            await CleanupAfterStartupFailureAsync(startupFailure);
            throw;
        }
        catch (Exception startupFailure) when (!NativeCqrsBoundaryErrors.IsNonFatal(startupFailure))
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

    internal void SetJobHarness(NativeSagaTimeoutJobHarness harness) => JobHarness = harness;

    private static InvalidOperationException NotInitialized(string member)
        => new($"Native saga timeout fixture member {member} is unavailable before initialization.");

    private async Task CleanupAfterStartupFailureAsync(Exception startupFailure)
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception cleanupFailure) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
        catch (Exception cleanupFailure) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
    }

}
