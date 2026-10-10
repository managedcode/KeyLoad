using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Joins original public work and removes only roots after real RF3 owners have stopped.</summary>
internal static class RequestCqrsPhaseFaultCleanup
{
    internal static async Task RunAsync(string root, bool rootOwned, RequestCqrsProbeFixture? controls,
        RequestCqrsRf3Wave? wave, bool waveStartupAttempted, RequestCqrsRf3Callers? caller,
        RequestCqrsRf3Callers? administrator,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, CancellationTokenSource? callerCancellation,
        CancellationTokenSource? scenarioDeadline, Task? sdkCall,
        Task<RequestCqrsFaultMcpObservation>? mcpCall, Guid armId, List<Exception> failures, IReadOnlyList<Guid>? additionalArms = null)
    {
        var cleanup = new List<Exception>();
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await StopAdmissionAndReleaseAsync(controls, discovery, callerCancellation, cleanup, deadline.Token)
            .ConfigureAwait(false);
        await JoinOriginalCallsAsync(sdkCall, mcpCall, cleanup).ConfigureAwait(false);
        await JoinProducerDisposalAsync(controls, discovery, armId, cleanup, deadline.Token).ConfigureAwait(false);
        foreach (var additional in additionalArms ?? [])
        { await JoinProducerDisposalAsync(controls, discovery, additional, cleanup, deadline.Token).ConfigureAwait(false); }
        await DisposeClientsAsync(caller, administrator, cleanup).ConfigureAwait(false);
        var waveStopped = await StopWaveAsync(wave, waveStartupAttempted, cleanup).ConfigureAwait(false);
        await DisposeControlsAsync(controls, waveStopped, cleanup).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => callerCancellation?.Dispose(), cleanup);
        ServerFailureObserver.Observe(() => scenarioDeadline?.Dispose(), cleanup);
        if (rootOwned && waveStopped && cleanup.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), cleanup); }
        failures.AddRange(cleanup);
    }

    private static async Task StopAdmissionAndReleaseAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, CancellationTokenSource? callerCancellation,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        ServerFailureObserver.Observe(() => controls?.StopAdmission(), failures);
        if (callerCancellation is not null)
        { await ServerFailureObserver.ObserveAsync(callerCancellation.CancelAsync, failures).ConfigureAwait(false); }
        if (controls is not null && discovery is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cancellationToken),
                failures).ConfigureAwait(false);
        }
    }

    private static async Task JoinOriginalCallsAsync(Task? sdkCall,
        Task<RequestCqrsFaultMcpObservation>? mcpCall, List<Exception> failures)
    {
        if (sdkCall is not null)
        { await ServerFailureObserver.ObserveAsync(() => sdkCall, failures).ConfigureAwait(false); }
        if (mcpCall is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcpCall, failures).ConfigureAwait(false); }
    }

    private static async Task JoinProducerDisposalAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Guid armId, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        if (controls is null || discovery is null || armId == Guid.Empty)
        { return; }
        var state = controls.ArmFor(armId);
        if (state.RequestId is null || state.ProducerDisposedSeen)
        { return; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.ProducerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
    }

    private static async Task DisposeClientsAsync(RequestCqrsRf3Callers? caller,
        RequestCqrsRf3Callers? administrator, List<Exception> failures)
    {
        if (caller is not null)
        { await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (administrator is not null)
        { await ServerFailureObserver.ObserveAsync(() => administrator.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
    }

    private static async Task<bool> StopWaveAsync(RequestCqrsRf3Wave? wave, bool startupAttempted,
        List<Exception> failures)
    {
        if (wave is null)
        { return !startupAttempted; }
        var previous = failures.Count;
        await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false);
        return failures.Count == previous;
    }

    private static async Task DisposeControlsAsync(RequestCqrsProbeFixture? controls, bool waveStopped,
        List<Exception> failures)
    {
        if (controls is not null && waveStopped)
        { await ServerFailureObserver.ObserveAsync(controls.DisposeAfterResourcesJoinedAsync, failures).ConfigureAwait(false); }
    }
}
