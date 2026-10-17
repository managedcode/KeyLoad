using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed partial class PartitionMovementLateNativeNode
{
    private const string UnjoinedApplication = "The native application has not joined; retain its original storage.";

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        var owner = storageOwner();
        if (startup is { } opening)
        {
            await opening.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        ServerFailureObserver.Observe(Application.Lifetime.StopApplication, failures);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var native = silo();
        var discovery = native.Discovery;
        await ServerFailureObserver.ObserveAsync(() => native.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (!native.HasJoinedRequestWork)
        {
            failures.Add(new InvalidOperationException(UnjoinedApplication));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(() => Application.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            storageStop ??= storageLifecycle.StopAsync(CancellationToken.None);
            await storageStop.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (owner.Closing is not { IsCompleted: true } || !owner.CanReleaseRoot)
        {
            failures.Add(new InvalidOperationException(UnjoinedApplication));
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationDisposal ??= Application.DisposeAsync().AsTask();
            await applicationDisposal.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (applicationDisposal is { IsCompletedSuccessfully: true })
        {
            ServerFailureObserver.Observe(() => ControlledPartitionMovementNativePublication.RequireClosed(native, discovery), failures);
            IsDisposed = native.HasJoinedRequestWork && discovery?.Read().TransportReady != true;
        }
        else
        {
            failures.Add(new InvalidOperationException(UnjoinedApplication));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task JoinShutdownAsync(Task original, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        Task? bounded = null;
        ServerFailureObserver.Observe(() => bounded = original.WaitAsync(cancellationToken), failures);
        if (bounded is not null)
        {
            await bounded.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        await ObserveTerminalAsync(original, bounded, failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ObserveTerminalAsync(Task original, Task? bounded,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        var waiting = new List<Exception>();
        if (bounded is not null && !ReferenceEquals(bounded, original)
            && bounded.IsCanceled && cancellationToken.IsCancellationRequested)
        {
            await ServerFailureObserver.ObserveAsync(() => bounded, waiting).ConfigureAwait(false);
        }
        var terminal = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, terminal).ConfigureAwait(false);
        foreach (var canceled in waiting.OfType<OperationCanceledException>())
        {
            if (canceled.CancellationToken == cancellationToken
                && !terminal.OfType<OperationCanceledException>().Any(error => error.CancellationToken == cancellationToken))
            {
                failures.Add(canceled);
            }
        }
        failures.AddRange(terminal);
    }
}
