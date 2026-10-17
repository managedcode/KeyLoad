using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutRuntimeLifecycle(NativeSagaTimeoutFixture fixture) : IAsyncDisposable
{
    private const string Unavailable = "The original native saga runtime is unavailable or closing.";
    private readonly Lock gate = new();
    private NativeSagaTimeoutRuntimeGeneration? generation;
    private Task? restarting;
    private Task? disposal;

    internal TestCluster Cluster => Current.Cluster;
    internal NativeRequestWorkOwner RequestWork => Current.RequestWork;
    private NativeSagaTimeoutRuntimeGeneration Current => generation ?? throw new InvalidOperationException(Unavailable);

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (disposal is not null || generation is not null)
            {
                throw new InvalidOperationException(Unavailable);
            }
            generation = new(fixture);
            return StartOwnedAsync(generation, cancellationToken);
        }
    }

    private static async Task StartOwnedAsync(NativeSagaTimeoutRuntimeGeneration current, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => current.StartAsync(token), failures);
        if (failures.Count != 0)
        {
            await ServerFailureObserver.ObserveAsync(current.StopAsync, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task RestartAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (disposal is not null)
            {
                throw new InvalidOperationException(Unavailable);
            }
            if (restarting is not null && !restarting.IsCompletedSuccessfully)
            {
                return restarting;
            }
            return restarting = RestartCoreAsync(Current, cancellationToken);
        }
    }

    private async Task RestartCoreAsync(NativeSagaTimeoutRuntimeGeneration previous, CancellationToken token)
    {
        await Task.Yield();
        await previous.StopAsync();
        token.ThrowIfCancellationRequested();
        Task starting;
        lock (gate)
        {
            if (disposal is not null)
            {
                throw new InvalidOperationException(Unavailable);
            }
            generation = new(fixture);
            starting = StartOwnedAsync(generation, token);
        }
        await starting;
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= DisposeCoreAsync(restarting));
        }
    }

    private async Task DisposeCoreAsync(Task? originalRestart)
    {
        await Task.Yield();
        var failures = new List<Exception>();
        if (originalRestart is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => originalRestart, failures);
        }
        if (generation is not null)
        {
            Task? originalDisposal = null;
            try
            {
                originalDisposal = generation.DisposeAsync().AsTask();
                await originalDisposal;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.AddRange(originalDisposal?.Exception is { } fault ? fault.InnerExceptions : [error]);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.AddRange(originalDisposal?.Exception is { } fault ? fault.InnerExceptions : [error]);
            }
        }
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(fixture.Database.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

/// <summary>Retains one original native generation and its terminal startup/cleanup evidence.</summary>
internal sealed class NativeSagaTimeoutRuntimeGeneration(NativeSagaTimeoutFixture fixture) : IAsyncDisposable
{
    private const int NativeSiloCount = 1;
    private const string Unavailable = "The native saga timeout generation is unavailable or closing.";
    private const string Unjoined = "The original native saga timeout work has not joined.";
    private readonly Lock gate = new();
    private TestCluster? cluster;
    private NativeRequestWorkOwner? requestWork;
    private Task? starting;
    private Task? stopping;
    private bool deployed;

    internal TestCluster Cluster => cluster ?? throw new InvalidOperationException(Unavailable);
    internal NativeRequestWorkOwner RequestWork => requestWork ?? throw new InvalidOperationException(Unavailable);

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
        requestWork = new NativeRequestWorkOwner(fixture.Routing);
        var coordinator = new EmbeddedCoordinator(fixture.Database.Database);
        var builder = CreateBuilder();
        cluster = builder.Build();
        fixture.SetRuntimeDependencies(
            new GrainRequestCodec(fixture.Database.Database, TimeProvider.System, fixture.Routing), coordinator);
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
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.AddRange(originalDisposal?.Exception is { } fault ? fault.InnerExceptions : [error]);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
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
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
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

}
