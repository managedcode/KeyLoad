using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
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
        Guid heldCommandId, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        var cleanup = new List<Exception>();
        async Task CleanupOwnedAsync()
        {
            using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
            if (operationDeadline is not null)
            {
                await ObserveAsync(operationDeadline.CancelAsync, cleanup, failureObserver,
                RequestCqrsLifecycleStage.AuthorityDeadlineCancellation).ConfigureAwait(false);
            }
            await ReleaseAndJoinAsync(controls, discovery, sdkCall, mcpCall, armId, originalStarted,
                cleanup, failureObserver, deadline.Token).ConfigureAwait(false);
            await DisposeCallersAsync(caller, administrator, cleanup, failureObserver).ConfigureAwait(false);
            var waveStopped = await StopWaveAsync(wave, waveStartupAttempted, cleanup, failureObserver)
                .ConfigureAwait(false);
            await DisposeControlsAsync(controls, waveStopped, cleanup, failureObserver).ConfigureAwait(false);
            var oracle = outcomeOracle;
            if (waveStopped && cleanup.Count == 0 && oracle is not null)
            {
                await ObserveAsync(() => InspectIfReadyAsync(oracle, heldCommandId, deadline.Token),
                    cleanup, failureObserver, RequestCqrsLifecycleStage.AuthorityOutcomeInspect).ConfigureAwait(false);
            }
            RequestCqrsLifecycleFailureObserver.Observe(() => operationDeadline?.Dispose(), cleanup,
                failureObserver, RequestCqrsLifecycleStage.AuthorityDeadlineDispose);
            if (rootCreated && waveStopped && cleanup.Count == 0)
            {
                RequestCqrsLifecycleFailureObserver.Observe(() => Directory.Delete(root, recursive: true), cleanup,
                failureObserver, RequestCqrsLifecycleStage.AuthorityRootDelete);
            }
        }
        await ObserveAsync(CleanupOwnedAsync, cleanup, failureObserver,
            RequestCqrsLifecycleStage.AuthorityCleanupDeadlineDispose).ConfigureAwait(false);
        failures.AddRange(cleanup);
    }

    private static async Task ReleaseAndJoinAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Task<Result<CommitReceipt>>? sdkCall,
        Task<RequestCqrsFaultMcpObservation>? mcpCall, Guid armId, bool originalStarted,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver,
        CancellationToken cancellationToken)
    {
        if (controls is not null)
        {
            RequestCqrsLifecycleFailureObserver.Observe(controls.StopAdmission, failures, failureObserver,
                RequestCqrsLifecycleStage.AuthorityAdmissionRelease);
            if (discovery is not null)
            {
                await ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, cancellationToken), failures,
                failureObserver, RequestCqrsLifecycleStage.AuthorityAdmissionRelease).ConfigureAwait(false);
            }
        }
        if (sdkCall is not null)
        {
            await ObserveAsync(() => sdkCall, failures, failureObserver,
            RequestCqrsLifecycleStage.AuthoritySdkJoin).ConfigureAwait(false);
        }
        if (mcpCall is not null)
        {
            await ObserveAsync(() => mcpCall, failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityMcpJoin).ConfigureAwait(false);
        }
        await ObserveProducerDisposedAsync(controls, discovery, armId, originalStarted, failures,
            failureObserver, cancellationToken).ConfigureAwait(false);
        if (controls is not null && armId != Guid.Empty)
        {
            await ObserveAsync(() => RetireArmIfOpenAsync(controls, armId, cancellationToken), failures,
            failureObserver, RequestCqrsLifecycleStage.AuthorityArmRetire).ConfigureAwait(false);
        }
    }

    private static async Task ObserveProducerDisposedAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Guid armId, bool originalStarted,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver,
        CancellationToken cancellationToken)
    {
        await ObserveAsync(async () =>
        {
            if (controls is null || armId == Guid.Empty || !originalStarted
                || controls.ArmFor(armId).ProducerDisposedSeen || discovery is null)
            { return; }
            _ = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.ProducerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
        }, failures, failureObserver, RequestCqrsLifecycleStage.AuthorityProducerDispose).ConfigureAwait(false);
    }

    private static async Task DisposeCallersAsync(RequestCqrsRf3Callers? caller,
        RequestCqrsRf3Callers? administrator, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        if (caller is not null)
        {
            await ObserveAsync(() => caller.DisposeAsync().AsTask(), failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityCallerDispose).ConfigureAwait(false);
        }
        if (administrator is not null)
        {
            await ObserveAsync(() => administrator.DisposeAsync().AsTask(), failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityAdministratorDispose).ConfigureAwait(false);
        }
    }

    private static async Task<bool> StopWaveAsync(RequestCqrsRf3Wave? wave, bool startupAttempted,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        if (wave is null)
        { return !startupAttempted; }
        var prior = failures.Count;
        await ObserveAsync(wave.StopAsync, failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityWaveStop).ConfigureAwait(false);
        return prior == failures.Count;
    }

    private static async Task DisposeControlsAsync(RequestCqrsProbeFixture? controls, bool waveStopped,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        if (controls is not null && waveStopped)
        {
            await ObserveAsync(controls.DisposeAfterResourcesJoinedAsync, failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityControlsDispose).ConfigureAwait(false);
        }
    }

    private static Task ObserveAsync(Func<Task> operation, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver, RequestCqrsLifecycleStage stage)
        => RequestCqrsLifecycleFailureObserver.ObserveAsync(operation, failures, failureObserver, stage);

    private static async Task RetireArmIfOpenAsync(RequestCqrsProbeFixture controls, Guid armId,
        CancellationToken cancellationToken)
    {
        if (!controls.ArmFor(armId).Retired)
        { await controls.RetireArmAsync(armId, cancellationToken).ConfigureAwait(false); }
    }

    private static Task InspectIfReadyAsync(RequestCqrsAuthorityOutcomeOracle outcomeOracle,
        Guid commandId, CancellationToken cancellationToken)
        => outcomeOracle.ReadyToInspect
            ? outcomeOracle.InspectAfterWaveJoinedAsync(commandId, cancellationToken)
            : Task.CompletedTask;
}
