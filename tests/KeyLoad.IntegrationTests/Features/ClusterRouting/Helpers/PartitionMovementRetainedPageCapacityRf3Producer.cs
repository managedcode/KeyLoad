using KeyLoad.Client;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only the real signed source unique read grain can claim the owning byte-refusal identity arm.</summary>
internal static class PartitionMovementRetainedPageCapacityRf3Producer
{
    private const string MissingRead = "The actual original transfer-data read did not reach its native source identity marker.";

    internal static async Task RequireDeniedAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            Guid.Empty, GrainReadKind.PartitionMovementTransferData, RequestCqrsProbePhase.TransferPageRetainedBudgetExceeded,
            RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var markerTask = wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.TransferPageRetainedBudgetExceeded,
            RequestCqrsProbeOutcome.Observed, discovery, caller.Token);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest with { Mode = PartitionMoveMode.Resume }, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(markerTask, call).ConfigureAwait(false);
            if (!markerTask.IsCompletedSuccessfully)
            {
                var early = await call.ConfigureAwait(false);
                await Assert.That(early.Problem?.ErrorCode).IsNull();
                throw new InvalidOperationException(MissingRead);
            }
            var marker = await markerTask.ConfigureAwait(false);
            await Assert.That(marker.CommandId).IsEqualTo(Guid.Empty);
            wave.QueryControls.WriteRelease(arm, marker.RequestId);
            var released = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.TransferPageRetainedBudgetExceeded,
                RequestCqrsProbeOutcome.Released, discovery, cancellationToken).ConfigureAwait(false);
            await Assert.That(released.RequestId).IsEqualTo(marker.RequestId);
            var actual = await call.ConfigureAwait(false);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await Assert.That(actual.Problem?.Detail).IsEqualTo(PartitionMovementProtocol.Unavailable);
            await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, marker.RequestId,
                Guid.Empty, discovery, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await markerTask.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinArmAsync(wave, arm, discovery, cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task JoinArmAsync(TwoRf3MembershipWave wave, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken cancellationToken)
        => wave.QueryControls.ArmFor(arm).RequestId is not null
            ? wave.QueryControls.WaitForSettlementAsync(arm, discovery, cancellationToken) : Task.CompletedTask;
}
