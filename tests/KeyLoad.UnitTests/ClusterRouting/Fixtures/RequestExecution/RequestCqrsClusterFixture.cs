using KeyLoad.Clustering;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed partial class RequestCqrsClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private const int ShutdownSeconds = 30;
    private const int StartupSeconds = 30;
    private const string RequestWorkNotJoined = "The fixture request work has not joined.";
    private readonly System.Threading.Lock disposalGate = new();
    private Task? disposal;
    private readonly Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime>? nativeFactory;
    private readonly Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeAnnMaintenanceTestRuntime>? nativeAnnFactory;
    private bool clusterStopped;
    private NativeRequestWorkOwner? requestWork;
    private readonly string ownerId = Guid.NewGuid().ToString("N");

    internal RequestCqrsClusterFixture(TimeProvider? clock = null, GrainRoutingOptions? routing = null,
        IGrainRequestPhaseObserver? observer = null,
        Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime>? nativeMaintenance = null,
        IGrainRequestPurposeObserver? purposeObserver = null,
        Func<TestDatabase, KeyLoad.UnitTests.Features.Search.NativeAnnMaintenanceTestRuntime>? nativeAnnMaintenance = null)
    {
        nativeFactory = nativeMaintenance;
        nativeAnnFactory = nativeAnnMaintenance;
        UsesNativeAdmission = nativeFactory is not null || nativeAnnFactory is not null;
        Clock = clock ?? TimeProvider.System;
        RoutingOptions = UnitRoutingOptions.Routing(routing);
        Database = new TestDatabase(timeProvider: Clock);
        try
        {
            RequestCqrsFixtureOwners.Register(ownerId, this);
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.Properties[RequestCqrsFixtureOwners.ConfigurationKey] = ownerId;
            builder.AddSiloBuilderConfigurator<RequestCqrsSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RequestCqrsClientConfigurator>();
            Cluster = builder.Build();
            Codec = new GrainRequestCodec(Database.Database, Clock, RoutingOptions) { PhaseObserver = observer, PurposeObserver = purposeObserver };
        }
        catch (Exception startupFailure)
        {
            RequestCqrsFixtureOwners.Release(ownerId);
            try
            {
                Database.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    internal TestCluster Cluster
    {
        get;
    }
    internal bool UsesNativeAdmission
    {
        get;
    }
    internal Guid ConnectionId { get; } = Guid.NewGuid();
    internal NativeRuntimeTestOptions Timing { get; } = new();
    internal GrainRequestCodec Codec
    {
        get;
    }
    internal TestDatabase Database
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal IOptions<GrainRoutingOptions> RoutingOptions
    {
        get;
    }
    internal KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime? NativeMaintenance
    {
        get; private set;
    }
    internal KeyLoad.UnitTests.Features.Search.NativeAnnMaintenanceTestRuntime? NativeAnnMaintenance
    {
        get; private set;
    }
    internal NativeRequestWorkOwner RequestWork => requestWork ??= new(RoutingOptions);

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(StartupSeconds), TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var logRoot = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        try
        {
            NativeMaintenance = nativeFactory?.Invoke(Database);
            NativeAnnMaintenance = nativeAnnFactory?.Invoke(Database);
            await Cluster.DeployAsync(startup.Token);
        }
        catch (Exception startupFailure)
        {
            await NativeClusterStartupEvidence.ThrowAfterCleanupAsync(startupFailure, Cluster,
                nameof(RequestCqrsClusterFixture), logRoot, cancellationToken.IsCancellationRequested,
                deadline.IsCancellationRequested, () => DisposeAsync().AsTask());
            throw;
        }
    }

    internal bool IsDisposalJoined
        => RequestCqrsFixtureDisposalRegistration.IsJoined(disposalGate, in disposal);

    public ValueTask DisposeAsync()
    {
        var task = GetDisposalTask();
        GC.SuppressFinalize(this);
        return new(task);
    }

    private Task GetDisposalTask()
        => RequestCqrsFixtureDisposalRegistration.Get(disposalGate, ref disposal, DisposeCoreAsync);

    private async Task DisposeCoreAsync(Task registered)
    {
        await registered;
        var failures = new List<Exception>();
        await StopClusterAsync(failures);
        if (requestWork is { IsJoined: false })
        {
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkNotJoined);
        }
        await DisposeOwnedResourcesAsync(failures);

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }

    }

    private async Task StopClusterAsync(List<Exception> failures)
    {
        if (requestWork is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures);
            if (!requestWork.IsJoined)
            {
                return;
            }
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSeconds), TimeProvider.System);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            async () =>
            {
                await Cluster.StopAllSilosAsync(deadline.Token);
                clusterStopped = true;
            }, failures);
    }

}
