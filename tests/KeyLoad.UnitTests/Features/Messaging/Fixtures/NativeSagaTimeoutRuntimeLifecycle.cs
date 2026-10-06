using KeyLoad.Core;
using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutRuntimeLifecycle(NativeSagaTimeoutFixture fixture) : IAsyncDisposable
{
    private const int NativeSiloCount = 1;
    private TestCluster? cluster;
    private NativeRequestWorkOwner? requestWork;
    private bool deployed;

    internal TestCluster Cluster => cluster ?? throw new InvalidOperationException(
        "The native saga timeout runtime is unavailable before startup.");

    internal NativeRequestWorkOwner RequestWork => requestWork ?? throw new InvalidOperationException(
        "The native saga timeout request owner is unavailable before startup.");

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(fixture.TestProfile.StartupTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        try
        {
            await StartRuntimeAsync(deadline.Token);
        }
        catch (Exception startupFailure) when (NativeCqrsBoundaryErrors.IsNonFatal(startupFailure))
        {
            await CleanupFailedStartAsync(startupFailure);
            throw;
        }
        catch (Exception startupFailure) when (!NativeCqrsBoundaryErrors.IsNonFatal(startupFailure))
        {
            await CleanupFailedStartAsync(startupFailure);
            throw;
        }
    }

    private async Task StartRuntimeAsync(CancellationToken cancellationToken)
    {
        requestWork = new NativeRequestWorkOwner(fixture.Routing);
        var coordinator = new EmbeddedCoordinator(fixture.Database.Database);
        var builder = CreateBuilder();
        cluster = builder.Build();
        fixture.SetRuntimeDependencies(
            new GrainRequestCodec(fixture.Database.Database, TimeProvider.System, fixture.Routing), coordinator);
        await DeployAndOpenAsync(cancellationToken);
    }

    private async Task CleanupFailedStartAsync(Exception startupFailure)
    {
        var failures = new List<Exception>();
        await StopAsync(failures, disposeDatabase: false);
        if (failures.Count > 0)
        {
            throw new AggregateException(startupFailure, new AggregateException(failures));
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    internal async Task RestartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await StopAsync(failures, disposeDatabase: false);
        NativeSagaTimeoutCleanup.ThrowFailures(failures);
        await StartAsync(cancellationToken);
    }

    internal async Task StopAsync(ICollection<Exception> failures, bool disposeDatabase)
    {
        using var deadline = new CancellationTokenSource(fixture.TestProfile.ShutdownTimeout, TimeProvider.System);
        await CloseSchedulingAsync(failures);
        await StopSilosAsync(failures, deadline.Token);
        await DrainRequestWorkAsync(failures);
        await DisposeClusterAsync(failures);
        await DisposeRequestWorkAsync(failures);
        if (disposeDatabase)
        {
            DisposeDatabase(failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await StopAsync(failures, disposeDatabase: true);
        NativeSagaTimeoutCleanup.ThrowFailures(failures);
    }

    private static TestClusterBuilder CreateBuilder()
    {
        var builder = new TestClusterBuilder(initialSilosCount: NativeSiloCount);
        builder.AddSiloBuilderConfigurator<NativeSagaTimeoutSiloConfigurator>();
        builder.AddClientBuilderConfigurator<NativeSagaTimeoutClientConfigurator>();
        return builder;
    }

    private async Task DeployAndOpenAsync(CancellationToken cancellationToken)
    {
        await Cluster.DeployAsync(cancellationToken);
        deployed = true;
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(
            view, NativeSagaTimeoutTestData.RootPrincipalId, TimeProvider.System.GetUtcNow()));
        var services = Cluster.GetSiloServiceProvider();
        var startup = new RuntimeJournalStartupRequests(Cluster.Client, fixture.Codec, fixture.Database.Database,
            fixture.Coordinator, services, TimeProvider.System, fixture.Routing);
        await startup.BootstrapAsync(principal, Guid.NewGuid(), cancellationToken);
        services.GetRequiredService<RuntimeJournalAdmission>().Open(cancellationToken);
        fixture.SetJobHarness(new NativeSagaTimeoutJobHarness(Cluster.Client, services, fixture.TestProfile));
    }

    private async Task CloseSchedulingAsync(ICollection<Exception> failures)
    {
        if (cluster is null || !deployed)
        {
            return;
        }

        await NativeSagaTimeoutCleanup.ObserveAsync(() =>
        {
            Cluster.GetSiloServiceProvider().GetRequiredService<RuntimeJournalAdmission>().CloseScheduling();
            return Task.CompletedTask;
        }, failures);
    }

    private async Task StopSilosAsync(ICollection<Exception> failures, CancellationToken cancellationToken)
    {
        if (cluster is null)
        {
            return;
        }

        await NativeSagaTimeoutCleanup.ObserveAsync(() => Cluster.StopAllSilosAsync(cancellationToken), failures);
        deployed = false;
    }

    private async Task DrainRequestWorkAsync(ICollection<Exception> failures)
    {
        if (requestWork is not null)
        {
            await NativeSagaTimeoutCleanup.ObserveAsync(requestWork.DrainAsync, failures);
        }
    }

    private async Task DisposeClusterAsync(ICollection<Exception> failures)
    {
        if (cluster is not null)
        {
            try
            {
                await cluster.DisposeAsync();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            finally
            {
                cluster = null;
            }
        }
    }

    private async Task DisposeRequestWorkAsync(ICollection<Exception> failures)
    {
        if (requestWork is not null)
        {
            try
            {
                await requestWork.DisposeAsync();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            finally
            {
                requestWork = null;
            }
        }
    }

    private void DisposeDatabase(ICollection<Exception> failures)
    {
        try
        {
            fixture.Database.Dispose();
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

}
