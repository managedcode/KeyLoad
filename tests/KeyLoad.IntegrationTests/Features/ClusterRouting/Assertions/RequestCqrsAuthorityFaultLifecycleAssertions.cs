using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Assertions;

internal static class RequestCqrsAuthorityFaultLifecycleAssertions
{
    internal static async Task VerifyReleasedAndDisposedAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, Guid armId, Guid commandId,
        RequestCqrsProbeMarkerRecord original, CancellationToken cancellationToken,
        Action<RequestCqrsLifecycleStage>? observeStage = null)
    {
        observeStage?.Invoke(RequestCqrsLifecycleStage.AuthorityReleasedMarkerWait);
        var released = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Released, discovery, cancellationToken).ConfigureAwait(false);
        await Assert.That(released.RequestId).IsEqualTo(original.RequestId);
        await Assert.That(released.CommandId).IsEqualTo(commandId);
        observeStage?.Invoke(RequestCqrsLifecycleStage.AuthorityProducerDisposedMarkerWait);
        var disposed = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
        await Assert.That(disposed.RequestId).IsEqualTo(original.RequestId);
        await Assert.That(disposed.CommandId).IsEqualTo(commandId);
        await Assert.That(disposed.SessionId).IsEqualTo(original.SessionId);
        await Assert.That(disposed.ArmId).IsEqualTo(original.ArmId);
        await Assert.That(disposed.Voter).IsEqualTo(original.Voter);
        await Assert.That(disposed.SiloAddress).IsEqualTo(original.SiloAddress);
        await Assert.That(disposed.EntryIndex).IsEqualTo(original.EntryIndex);
        await Assert.That(disposed.EntryTerm).IsEqualTo(original.EntryTerm);
        await Assert.That(controls.ArmFor(armId).ProducerDisposedSeen).IsTrue();
    }
    internal static async Task VerifyRevocationStagesAsync(RequestCqrsLifecycleSnapshot snapshot)
    {
        await Assert.That(snapshot.Node1Readiness).IsEqualTo(RequestCqrsNodeReadinessOutcome.Ready);
        await Assert.That(snapshot.Node2Readiness).IsEqualTo(RequestCqrsNodeReadinessOutcome.Ready);
        await Assert.That(snapshot.Node3Readiness).IsEqualTo(RequestCqrsNodeReadinessOutcome.Ready);
        await Assert.That(snapshot.HeldWriteObserved).IsTrue();
        await Assert.That(snapshot.PersistedRevocationEntered).IsTrue();
    }

}
