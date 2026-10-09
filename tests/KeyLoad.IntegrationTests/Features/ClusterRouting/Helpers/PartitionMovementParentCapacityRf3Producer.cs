using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns the genuine first StageGrant before-submit caller/probe, retaining its actual native admitted original.</summary>
internal static class PartitionMovementParentCapacityRf3Producer
{
    private const string Administrator = "root";

    internal static async Task<Guid> HoldAndJoinAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var identity = PartitionMovementParentPhaseIds.For(seed.FirstRequest, Administrator,
            PartitionMovementParentPhaseRole.StageGrant);
        var signed = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var arm = wave.QueryControls.WriteArm(Administrator, identity, null,
            RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var marker = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.BeforeSubmit,
                RequestCqrsProbeOutcome.Observed, signed, cancellationToken).ConfigureAwait(false);
            await Assert.That(marker.CommandId).IsEqualTo(identity);
            await caller.CancelAsync().ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, marker.RequestId,
                identity, signed, cancellationToken).ConfigureAwait(false);
            var actual = await call.ConfigureAwait(false);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(signed,
            cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return identity;
    }
}
