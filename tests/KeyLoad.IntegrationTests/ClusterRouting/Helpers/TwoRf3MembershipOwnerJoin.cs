using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipOwnerJoin
{
    internal static async Task JoinAsync(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.queryControls is { } controls)
        {
            ServerFailureObserver.Observe(controls.StopAdmission, failures);
        }
        NativeMembershipDiagnosticWaveLifecycle.StopAdmission(wave, failures);
        NativeBlobWireWaveLifecycle.StopAdmission(wave, failures);
        if (wave.IsolationOwner is { } namespaceOwner)
        {
            await ServerFailureObserver.ObserveAsync(() => namespaceOwner.RestoreAsync(CancellationToken.None), failures);
        }
        await wave.DisposeApplicationAsync(failures).ConfigureAwait(false);
        if (wave.CanCheckLocks)
        {
            ServerFailureObserver.Observe(wave.AssertAllNodeLocksReleased, failures);
        }
        await NativeCanonicalModelCutWaveLifecycle.JoinAsync(wave, failures).ConfigureAwait(false);
        if (wave.applicationDisposed && wave.nodeLocksReleased && wave.queryControls is { } joinedControls)
        {
            await ServerFailureObserver.ObserveAsync(joinedControls.DisposeAfterResourcesJoinedAsync, failures).ConfigureAwait(false);
        }
        if (wave.IsolationOwner is { } retiredNamespace)
        {
            await ServerFailureObserver.ObserveAsync(() => retiredNamespace.SettleAfterStopAsync(
            wave.applicationDisposed && wave.nodeLocksReleased, CancellationToken.None), failures);
        }
        if (wave.applicationDisposed && wave.nodeLocksReleased && wave.frameObservation is { } joinedFrames)
        {
            ServerFailureObserver.Observe(() => joinedFrames.DeleteAfterOwnerJoin(wave), failures);
        }
        if (wave.applicationDisposed && wave.nodeLocksReleased && wave.nativeDiscoveryOmission is { } joinedDiscovery)
        {
            await ServerFailureObserver.ObserveAsync(() => joinedDiscovery.DisposeAfterOwnersJoinedAsync(wave), failures).ConfigureAwait(false);
        }
        await NativeMembershipDiagnosticWaveLifecycle.JoinAsync(wave, failures).ConfigureAwait(false);
        NativeBlobWireWaveLifecycle.DeleteJoined(wave, failures);
        await TwoRf3MembershipLocalImageLifetime.JoinAsync(wave, failures).ConfigureAwait(false);
    }

}
