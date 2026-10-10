using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3DeadlineProbe(RequestCqrsProbeFixture controls,
    IReadOnlyList<ReplicaSiloDiscovery> discovery)
{
    private Guid parentArm;
    private Guid childArm;
    internal Guid ParentRequest { get; private set; }

    internal void Arm(string principal)
    {
        parentArm = controls.WriteArm(principal, Guid.Empty, GrainReadKind.DistributedSearch,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        childArm = controls.WriteArm(principal, Guid.Empty, GrainReadKind.DistributedSearchLeaf,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold, RemotePartitionQueryRf3Seed.Local);
    }

    internal async Task ObserveNaturalDeadlineAsync(CancellationToken token)
    {
        var parent = await controls.WaitForMarkerAsync(parentArm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        ParentRequest = parent.RequestId;
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(parent, parentArm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, discovery).ConfigureAwait(false);
        controls.WriteRelease(parentArm, ParentRequest);
        var child = await controls.WaitForMarkerAsync(childArm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(child, childArm, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, discovery).ConfigureAwait(false);
        await Assert.That(child.RequestId).IsNotEqualTo(ParentRequest);
        // No caller cancellation or child release: the original native query deadline owns settlement.
        var cancelled = await controls.WaitForMarkerAsync(childArm, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Cancelled, discovery, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(child.RequestId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, childArm, child.RequestId,
            Guid.Empty, discovery, token).ConfigureAwait(false);
        await ParentDisposedAsync(token).ConfigureAwait(false);
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await Assert.That(controls.ArmFor(childArm).ReleaseWritten).IsFalse();
        await controls.RetireArmAsync(childArm, token).ConfigureAwait(false);
        await controls.RetireArmAsync(parentArm, token).ConfigureAwait(false);
    }

    internal async Task ParentDisposedAsync(CancellationToken token)
    {
        var marker = await controls.WaitForMarkerAsync(parentArm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        await Assert.That(marker.RequestId).IsEqualTo(ParentRequest);
        await Assert.That(controls.ArmFor(parentArm).ProducerDisposedSeen).IsTrue();
    }
}
