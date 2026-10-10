using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3Stages
{
    internal static async Task<IReadOnlyList<KeyLoad.Orleans.ReplicaSiloDiscovery>> DiscoveryAsync(
        TwoRf3MembershipWave wave, CancellationToken token)
    {
        var result = new List<KeyLoad.Orleans.ReplicaSiloDiscovery>();
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
            RequestCqrsRf3Protocol.Node3 })
        {
            result.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
            node, wave.Profile, token).ConfigureAwait(false));
        }
        return result;
    }

    internal static async Task RequireCancelledAndDisposedAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<KeyLoad.Orleans.ReplicaSiloDiscovery> originalDiscovery, Guid arm,
        RequestCqrsProbeMarkerRecord original, CancellationToken token)
    {
        var cancelled = await controls.WaitForMarkerAsync(arm, original.Phase,
            RequestCqrsProbeOutcome.Cancelled, originalDiscovery, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(original.RequestId);
        await Assert.That(cancelled.CommandId).IsEqualTo(original.CommandId);
        var disposed = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, originalDiscovery, token).ConfigureAwait(false);
        await Assert.That(disposed.RequestId).IsEqualTo(original.RequestId);
        await Assert.That(disposed.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(controls.ArmFor(arm).ProducerDisposedSeen).IsTrue();
        await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
    }
}
