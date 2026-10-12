using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeMembershipDiagnosticWaveLifecycle
{
    internal static void PrepareControls(TwoRf3MembershipWave wave, string root, CancellationToken token)
    {
        if (wave.queryProbe && wave.queryControls is null)
        { wave.queryControls = RequestCqrsProbeFixture.Create(root, Guid.NewGuid()); }
        if (wave.nativeDiscoveryOmissionSelected && wave.nativeDiscoveryOmission is null)
        { wave.nativeDiscoveryOmission = KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture.Create(root, token); }
        if (wave.nativeMembershipDiagnostics && wave.nativeMembershipControls is null)
        { wave.nativeMembershipControls = NativeMembershipDiagnosticFixture.Create(root, Guid.NewGuid()); }
    }

    internal static async Task<string> StopForDirectoryReadAsync(TwoRf3MembershipWave wave)
    {
        var failures = new List<Exception>();
        await wave.DisposeApplicationAsync(failures).ConfigureAwait(false);
        if (wave.CanCheckLocks)
        { ServerFailureObserver.Observe(wave.AssertAllNodeLocksReleased, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        if (!wave.nodeLocksReleased)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        return wave.dataRoot ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.InvalidRoot);
    }

    internal static void StopAdmission(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.nativeMembershipControls is { } controls)
        { ServerFailureObserver.Observe(controls.StopAdmission, failures); }
    }

    internal static async Task JoinAsync(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.nativeMembershipControls is not { } controls)
        { return; }
        if (!wave.applicationDisposed || !wave.nodeLocksReleased)
        {
            wave.RetainRoots();
            failures.Add(new InvalidOperationException(TwoRf3MembershipProtocol.MissingState));
            return;
        }
        var before = failures.Count;
        await ServerFailureObserver.ObserveAsync(() => controls.DisposeAfterOwnersJoinedAsync(wave), failures)
            .ConfigureAwait(false);
        if (failures.Count != before)
        { wave.RetainRoots(); }
    }
}
