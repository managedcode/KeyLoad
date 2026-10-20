using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Retains one original execution generation and its terminal startup/cleanup evidence.</summary>
internal sealed class SagaTimeoutRuntimeGeneration(SagaTimeoutFixture fixture) : IAsyncDisposable
{
    private const int RuntimeSiloCount = 1;
    private const string Unavailable = "The execution saga timeout generation is unavailable or closing.";
    private const string Unjoined = "The original execution saga timeout work has not joined.";
    private readonly Lock gate = new();
    private TestCluster? cluster;
    private RequestWorkOwner? requestWork;
    private Task? starting;
    private Task? stopping;
    private bool deployed;

    internal TestCluster Cluster => cluster ?? throw new InvalidOperationException(Unavailable);
    internal RequestWorkOwner RequestWork => requestWork ?? throw new InvalidOperationException(Unavailable);

    internal Task StartAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (starting is not null || stopping is not null)
            {
                throw new InvalidOperationException(Unavailable);
            }
            return starting = StartCoreAsync(token);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(fixture.TestProfile.StartupTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        await Task.Yield();
        requestWork = new RequestWorkOwner(fixture.Routing);
        var coordinator = new EmbeddedCoordinator(fixture.Database.Database);
        var builder = CreateBuilder();
        cluster = builder.Build();
        fixture.SetRuntimeDependencies(
            new GrainRequestCodec(fixture.Database.Database.Store.Identity.Incarnation, fixture.Database.Database.Store.Identity.SigningKey,
            fixture.Database.Database.OperationLimitsOptions, TimeProvider.System, fixture.Routing), coordinator);
        await DeployAndOpenAsync(deadline.Token);
    }

    internal Task StopAsync()
    {
        lock (gate)
        {
            return stopping ??= StopCoreAsync(starting);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopCoreAsync(Task? originalStart)
    {
        using var deadline = new CancellationTokenSource(fixture.TestProfile.ShutdownTimeout, TimeProvider.System);
        await Task.Yield();
        var failures = new List<Exception>();
        if (originalStart is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => originalStart, failures);
        }
        if (cluster is { } originalCluster)
        {
            if (deployed)
            {
                ServerFailureObserver.Observe(() => originalCluster.GetSiloServiceProvider()
                    .GetRequiredService<RuntimeJournalAdmission>().CloseScheduling(), failures);
            }
            await ServerFailureObserver.ObserveAsync(() => originalCluster.StopAllSilosAsync(deadline.Token), failures);
        }
        if (requestWork is { } originalWork)
        {
            await ServerFailureObserver.ObserveAsync(originalWork.DrainAsync, failures);
        }
        if (cluster is not null)
        {
            Task? originalDisposal = null;
            try
            {
                originalDisposal = cluster.DisposeAsync().AsTask();
                await originalDisposal;
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.AddRange(originalDisposal?.Exception is { } fault ? fault.InnerExceptions : [error]);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.AddRange(originalDisposal?.Exception is { } fault ? fault.InnerExceptions : [error]);
            }
        }
        if (requestWork is not null)
        {
            try
            {
                await requestWork.DisposeAsync();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            if (!requestWork.IsJoined)
            {
                failures.Add(new InvalidOperationException(Unjoined));
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static TestClusterBuilder CreateBuilder()
    {
        var builder = new TestClusterBuilder(initialSilosCount: RuntimeSiloCount);
        builder.AddSiloBuilderConfigurator<SagaTimeoutSiloConfigurator>();
        builder.AddClientBuilderConfigurator<SagaTimeoutClientConfigurator>();
        return builder;
    }

    private async Task DeployAndOpenAsync(CancellationToken cancellationToken)
    {
        await Cluster.DeployAsync(cancellationToken);
        deployed = true;
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(
            view, SagaTimeoutTestData.RootPrincipalId, TimeProvider.System.GetUtcNow()));
        var services = Cluster.GetSiloServiceProvider();
        var startup = new RuntimeJournalStartupRequests(Cluster.Client, fixture.Codec,
            services, TimeProvider.System, fixture.Routing);
        await startup.BootstrapAsync(principal, Guid.NewGuid(), cancellationToken);
        services.GetRequiredService<RuntimeJournalAdmission>().Open(cancellationToken);
        fixture.SetJobHarness(new SagaTimeoutJobHarness(Cluster.Client, services, fixture.TestProfile));
    }

}
