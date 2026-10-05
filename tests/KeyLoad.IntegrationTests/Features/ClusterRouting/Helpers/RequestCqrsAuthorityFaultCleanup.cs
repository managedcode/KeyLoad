using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsAuthorityFaultCleanup
{
    internal static async Task RunAsync(string root, bool rootCreated, bool waveStartupAttempted,
        RequestCqrsProbeFixture? controls, RequestCqrsRf3Wave? wave,
        RequestCqrsRf3Callers? caller, RequestCqrsRf3Callers? administrator,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, CancellationTokenSource? operationDeadline,
        Task<Result<CommitReceipt>>? sdkCall, Task<RequestCqrsFaultMcpObservation>? mcpCall,
        Guid armId, bool originalStarted, RequestCqrsAuthorityOutcomeOracle? outcomeOracle,
        Guid heldCommandId, List<Exception> failures)
    {
        var cleanup = new List<Exception>();
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline);
        if (operationDeadline is not null)
        { await ServerFailureObserver.ObserveAsync(operationDeadline.CancelAsync, cleanup).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(() => ReleaseAndJoinAsync(controls, discovery, sdkCall,
            mcpCall, armId, originalStarted, cleanup, deadline.Token), cleanup).ConfigureAwait(false);
        await DisposeCallersAsync(caller, administrator, cleanup).ConfigureAwait(false);
        var waveStopped = await StopWaveAsync(wave, waveStartupAttempted, cleanup).ConfigureAwait(false);
        await DisposeControlsAsync(controls, waveStopped, cleanup).ConfigureAwait(false);
        if (waveStopped && cleanup.Count == 0 && outcomeOracle is { ReadyToInspect: true })
        {
            await ServerFailureObserver.ObserveAsync(
                () => outcomeOracle.InspectAfterWaveJoinedAsync(heldCommandId, deadline.Token), cleanup)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(() => operationDeadline?.Dispose(), cleanup);
        if (rootCreated && waveStopped && cleanup.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), cleanup); }
        failures.AddRange(cleanup);
    }

    private static async Task ReleaseAndJoinAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Task<Result<CommitReceipt>>? sdkCall,
        Task<RequestCqrsFaultMcpObservation>? mcpCall, Guid armId, bool originalStarted,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        if (controls is not null)
        {
            ServerFailureObserver.Observe(controls.StopAdmission, failures);
            if (discovery is not null)
            { await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cancellationToken), failures).ConfigureAwait(false); }
        }
        if (sdkCall is not null)
        { await ServerFailureObserver.ObserveAsync(() => sdkCall, failures).ConfigureAwait(false); }
        if (mcpCall is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcpCall, failures).ConfigureAwait(false); }
        await ObserveProducerDisposedAsync(controls, discovery, armId, originalStarted, failures, cancellationToken).ConfigureAwait(false);
        if (controls is not null && armId != Guid.Empty)
        { await ServerFailureObserver.ObserveAsync(() => controls.RetireArmAsync(armId, cancellationToken), failures).ConfigureAwait(false); }
    }

    private static async Task ObserveProducerDisposedAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Guid armId, bool originalStarted,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        if (controls is null || armId == Guid.Empty || !originalStarted
            || controls.ArmFor(armId).ProducerDisposedSeen || discovery is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.ProducerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
    }

    private static async Task DisposeCallersAsync(RequestCqrsRf3Callers? caller,
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
        var prior = failures.Count;
        await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false);
        return prior == failures.Count;
    }

    private static async Task DisposeControlsAsync(RequestCqrsProbeFixture? controls, bool waveStopped,
        List<Exception> failures)
    {
        if (controls is not null && waveStopped)
        { await ServerFailureObserver.ObserveAsync(controls.DisposeAfterResourcesJoinedAsync, failures).ConfigureAwait(false); }
    }
}
