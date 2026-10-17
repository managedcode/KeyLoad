using KeyLoad.Clustering;
using KeyLoad.Server;
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
