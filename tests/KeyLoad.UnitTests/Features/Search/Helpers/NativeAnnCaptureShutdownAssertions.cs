using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnCaptureShutdownAssertions
{
    private const int Count = 3;

    internal static async Task RunAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, QueryObservedWorkClock clock)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var token = TestContext.Current!.Execution.CancellationToken;
        var before = database.Store.GetReadDiagnostics();
        clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
            () => { entered.TrySetResult(); release.Wait(token); });
        Task<AnnMaintenanceCapabilityResult>? originalCapture = null;
        Task? shutdown = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                originalCapture = Task.Run(() => runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin,
                    token: token), CancellationToken.None);
                await entered.Task.WaitAsync(token);
                shutdown = runtime.Owner.DisposeAsync().AsTask();
                await Assert.That(shutdown.IsCompleted).IsFalse();
                var denied = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => runtime.PhaseAsync(database,
                    request, AnnMaintenanceCapabilityKind.Begin, token: token));
                await Assert.That(denied).IsNotNull();
            }, failures);
        }
        finally
        {
            release.Set();
            clock.Disarm();
            if (originalCapture is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => AssertOriginalCaptureClosedAsync(originalCapture), failures);
            }
            if (shutdown is not null)
            { await ServerFailureObserver.ObserveAsync(() => shutdown, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(clock.Triggered).IsTrue();
    }

    private static async Task AssertOriginalCaptureClosedAsync(Task capture)
    {
        var closed = await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => capture);
        await Assert.That(closed).IsNotNull();
    }

    internal static async Task HealthyAsync(TestDatabase database, AnnMaintenanceRequest request)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var result = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
                await Assert.That(result.Count).IsEqualTo(Count);
                await Assert.That(result.IndexSha256).IsNotNull();
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
