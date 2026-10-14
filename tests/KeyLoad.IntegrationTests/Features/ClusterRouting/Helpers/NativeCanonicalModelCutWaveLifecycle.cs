using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutWaveLifecycle
{
    internal static void StopAdmission(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.canonicalCutControls is not { } controls)
        {
            return;
        }
        var before = failures.Count;
        ServerFailureObserver.Observe(controls.StopAdmission, failures);
        if (failures.Count != before)
        {
            wave.RetainRoots();
        }
    }

    internal static async Task JoinAsync(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.canonicalCutControls is not { } controls)
        {
            return;
        }
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
        {
            wave.RetainRoots();
        }
    }
}
