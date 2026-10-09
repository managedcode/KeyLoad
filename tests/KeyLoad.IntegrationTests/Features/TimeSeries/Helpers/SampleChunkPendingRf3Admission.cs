using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Admission
{
    internal static async Task AdmitUnscheduledAsync(TwoRf3MembershipWave wave, SampleChunkPendingRf3Item item,
        KeyLoadClient administrator, IReadOnlyList<KeyLoad.Orleans.ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(item.Scope.Creator.Id, item.CommandId, null,
            RequestCqrsProbePhase.SampleChunkAdmissionPersisted, RequestCqrsProbeAction.ThrowOrdinary);
        await SampleChunkPendingRf3Seed.CorrectAsync(item, administrator, token).ConfigureAwait(false);
        var persisted = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SampleChunkAdmissionPersisted,
            RequestCqrsProbeOutcome.FaultRequested, discovery, token).ConfigureAwait(false);
        await Assert.That(persisted.CommandId).IsEqualTo(item.CommandId);
        var disposed = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await Assert.That(disposed.RequestId).IsEqualTo(persisted.RequestId);
        await Assert.That(disposed.CommandId).IsEqualTo(item.CommandId);
        await Assert.That(controls.ArmFor(arm).ProducerDisposedSeen).IsTrue();
        await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Assertions.PendingAsync(item, administrator, token).ConfigureAwait(false);
    }

    internal static async Task CorrectThenRefusedAsync(TwoRf3MembershipWave wave, SampleChunkPendingRf3Item item,
        KeyLoadClient administrator, CancellationToken token)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = SampleChunkPendingRf3Log.RequireAsync(wave, item.CommandId, reading.Token);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await SampleChunkPendingRf3Seed.CorrectAsync(item, administrator, token).ConfigureAwait(false);
            await original.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        KeyLoad.Server.ServerFailureObserver.Observe(reading.Cancel, failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await SampleChunkPendingRf3Assertions.PendingAsync(item, administrator, token).ConfigureAwait(false);
    }
}
