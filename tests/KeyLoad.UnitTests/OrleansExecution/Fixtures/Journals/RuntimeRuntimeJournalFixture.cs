#pragma warning disable ORLEANSEXP005
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RuntimeRuntimeJournalFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string Unjoined = "The original execution journal borrowers have not joined; retain their database root.";
    private readonly Lock disposalGate = new();
    private Task? disposalTask;
    internal readonly TestDatabase Database = new();
    internal readonly IOptions<RuntimeJournalOptions> JournalOptions = Options.Create(new RuntimeJournalOptions());
    internal readonly IOptions<RuntimeTestOptions> TimingOptions = Options.Create(new RuntimeTestOptions());
    private RequestWorkOwner? requestWork;

    internal RuntimeRuntimeJournalFixture()
    {
        if (!TimingOptions.Value.IsValid())
        {
            throw new InvalidOperationException("The execution runtime test timing profile is invalid.");
        }
        Database.Database.ConfigureRuntimeJournal(JournalOptions);
        Current = this;
        try
        {
            var builder = new TestClusterBuilder(initialSilosCount: 1);
            builder.AddSiloBuilderConfigurator<RuntimeJournalSiloConfigurator>();
            builder.AddClientBuilderConfigurator<RuntimeJournalClientConfigurator>();
            Cluster = builder.Build();
            Codec = new(Database.Database.Store.Identity.Incarnation, Database.Database.Store.Identity.SigningKey,
                Database.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        }
        catch (Exception failure)
        {
            Current = null;
            try
            {
                Database.Dispose();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                throw new AggregateException(failure, cleanup);
            }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                throw new AggregateException(failure, cleanup);
            }

            throw;
        }
    }

    internal TestCluster Cluster
    {
        get;
    }
    internal GrainRequestCodec Codec
    {
        get;
    }
    internal RequestWorkOwner RequestWork => requestWork ??= new(UnitRoutingOptions.Routing());

    [AllowNull]
    internal static RuntimeRuntimeJournalFixture Current
    {
        get => field ?? throw new InvalidOperationException("The execution runtime journal fixture is not active."); private set;
    }

    internal IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();
    internal IJournalStorageProvider Provider => SiloServices.GetRequiredService<IJournalStorageProvider>();
    internal IJournalStorageCatalog Catalog => SiloServices.GetRequiredService<IJournalStorageCatalog>();

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TimingOptions.Value.StartupTimeout, TimeProvider.System);
        try
        {
            await Cluster.DeployAsync(deadline.Token);
            await BootstrapAsync(deadline.Token);
            SiloServices.GetRequiredService<RuntimeJournalAdmission>().Open(deadline.Token);
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

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposalTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Publish the original task before execution cancellation callbacks can reenter.
        await Task.Yield();
        var failures = new List<Exception>();
        using (var deadline = new CancellationTokenSource(TimingOptions.Value.ShutdownTimeout, TimeProvider.System))
        {
            await ServerFailureObserver.ObserveAsync(() => Cluster.StopAllSilosAsync(deadline.Token), failures);
        }
        if (requestWork is not null)
        {
            await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
        if (requestWork is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        if (requestWork is { IsJoined: false } && failures.Count == 0)
        {
            failures.Add(new InvalidOperationException(Unjoined));
        }
        if (failures.Count == 0)
        {
            try
            {
                Database.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        if (failures.Count == 0)
        {
            Current = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var principal = Database.Store.Read(view => Database.Database.Principal(view, "root", TimeProvider.System.GetUtcNow()));
        var services = SiloServices;
        var startup = new RuntimeJournalStartupRequests(services.GetRequiredService<IGrainFactory>(), Codec,
            services, TimeProvider.System,
            UnitRoutingOptions.Routing());
        await startup.BootstrapAsync(principal, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
    }
}

#pragma warning restore ORLEANSEXP005
