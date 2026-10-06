using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.Messaging;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class NativeSagaTimeoutDataSourceAttribute : DataSourceGeneratorAttribute<NativeSagaTimeoutFixture>
{
    protected override IEnumerable<Func<NativeSagaTimeoutFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<NativeSagaTimeoutFixture>(
            SharedType.PerTestSession, metadata, null, static () => new NativeSagaTimeoutFixture());
    }
}

internal sealed class NativeSagaTimeoutFixture : IAsyncInitializer, IAsyncDisposable
{
    private const int NativeSiloCount = 1;
    private const string RootPrincipalId = "root";
    private const string InvalidProfileDetail = "The native runtime test timing profile is invalid.";
    private readonly IOptions<NativeRuntimeTestOptions> profile = Options.Create(new NativeRuntimeTestOptions());
    private TestCluster? cluster;
    private NativeRequestWorkOwner? requestWork;
    private bool deployed;
    private bool disposed;

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
    }

    internal TestDatabase Database { get; }
    internal TestCluster Cluster => cluster ?? throw NotInitialized(nameof(Cluster));
    internal GrainRequestCodec Codec { get => field ?? throw NotInitialized(nameof(Codec)); private set; }
    internal ICommitCoordinator Coordinator { get => field ?? throw NotInitialized(nameof(Coordinator)); private set; }
    internal NativeRequestWorkOwner RequestWork => requestWork ?? throw NotInitialized(nameof(RequestWork));
    internal NativeSagaTimeoutJobHarness JobHarness { get => field ?? throw NotInitialized(nameof(JobHarness)); private set; }
    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IOptions<GrainRoutingOptions> Routing { get; } = UnitRoutingOptions.Routing();
    internal IOptions<RuntimeJournalOptions> JournalOptions { get; } = Options.Create(new RuntimeJournalOptions());
    internal IOptions<NativeDurableJobOptions> DurableJobOptions { get; }
    internal IOptions<DueCoordinationOptions> DueOptions { get; } = Options.Create(new DueCoordinationOptions());
    internal NativeRuntimeTestOptions TestProfile => profile.Value;

    [AllowNull]
    internal static NativeSagaTimeoutFixture Current { get => field ?? throw new InvalidOperationException("The native saga timeout fixture is not active."); private set; }

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TestProfile.StartupTimeout);
        try
        {
            Database.Store.RequireReaderContract(KeyLoad.Storage.StoreReaderContract.RuntimeJournal);
            Database.Database.ConfigureRuntimeJournal(JournalOptions);
            requestWork = new NativeRequestWorkOwner(Routing);
            Coordinator = new EmbeddedCoordinator(Database.Database);
            Database.Configure("jobs", ResourceKind.WorkQueue);
            Database.Configure("timeouts", ResourceKind.WorkQueue);
            Current = this;
            var builder = new TestClusterBuilder(initialSilosCount: NativeSiloCount);
            builder.AddSiloBuilderConfigurator<NativeSagaTimeoutSiloConfigurator>();
            builder.AddClientBuilderConfigurator<NativeSagaTimeoutClientConfigurator>();
            cluster = builder.Build();
            Codec = new GrainRequestCodec(Database.Database, TimeProvider.System, Routing);
            await Cluster.DeployAsync(deadline.Token);
            deployed = true;
            var principal = Database.Store.Read(view => Database.Database.Principal(
                view, RootPrincipalId, TimeProvider.System.GetUtcNow()));
            var startup = new RuntimeJournalStartupRequests(Cluster.Client, Codec, Database.Database,
                Coordinator, SiloServices, TimeProvider.System, Routing);
            await startup.BootstrapAsync(principal, Guid.NewGuid(), deadline.Token);
            SiloServices.GetRequiredService<RuntimeJournalAdmission>().Open(deadline.Token);
            JobHarness = new NativeSagaTimeoutJobHarness(Cluster.Client, SiloServices, TestProfile);
        }
        catch (Exception startupFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        var failures = new List<Exception>();
        await StopRuntimeAsync(failures);
        await DisposeResourcesAsync(failures);
        Current = null;
        ThrowCleanupFailures(failures);
    }

    internal Task<DueDispatchResult> DispatchAsync(DueWorkHint hint, CancellationToken cancellationToken)
        => Cluster.Client.GetGrain<IRecurringDueCoordinatorGrain>(hint.Lane.Partition.AtomicPartitionId)
            .ProcessDueAsync(hint, cancellationToken);

    private static InvalidOperationException NotInitialized(string member)
        => new($"Native saga timeout fixture member {member} is unavailable before initialization.");

    private async Task StopRuntimeAsync(ICollection<Exception> failures)
    {
        if (cluster is null)
        {
            return;
        }
        if (deployed)
        {
            await ObserveAsync(() =>
            {
                SiloServices.GetRequiredService<RuntimeJournalAdmission>().CloseScheduling();
                return Task.CompletedTask;
            }, failures);
        }
        using var deadline = new CancellationTokenSource(TestProfile.ShutdownTimeout);
        await ObserveAsync(() => Cluster.StopAllSilosAsync(deadline.Token), failures);
        await ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
    }

    private async Task DisposeResourcesAsync(ICollection<Exception> failures)
    {
        if (requestWork is not null)
        {
            await ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        try
        {
            Database.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private static async Task ObserveAsync(Func<Task> operation, ICollection<Exception> failures)
    {
        try
        {
            await operation();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private static void ThrowCleanupFailures(IReadOnlyCollection<Exception> failures)
    {
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
