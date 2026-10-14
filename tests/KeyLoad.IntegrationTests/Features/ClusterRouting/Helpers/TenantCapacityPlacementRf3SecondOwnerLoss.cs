using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3SecondOwnerLoss
{
    private const int First = 0;
    private const int Second = 1;

    internal static async Task<PlacementIntentV1> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest[] moves, PlacementIntentV1 pending, Guid id,
        ApplyPlacementIntentRequest request, CancellationToken token)
    {
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var phase = PartitionMovementParentPhaseIds.For(moves[Second], pending.PrincipalId,
            PartitionMovementParentPhaseRole.Prepare);
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(pending.PrincipalId, phase, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold, moves[Second].Partition);
        var owner = wave.Application;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var observer = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = seeds[First].Source.ApplyPlacementIntentAsync(id, request, caller.Token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery, observer.Token);
        TenantCapacityPlacementRf3OwnerLossHeld.State? held = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            held = await TenantCapacityPlacementRf3SecondOwnerLossHeld.CaptureAsync(seeds, state, moves,
                pending, id, request, controls, discovery, original, marker, token);
            await PhysicalShardPartitionMovementRf3FaultOwner.KillAndJoinAsync(
                wave, TwoRf3MembershipProtocol.Node1, token);
            await TenantCapacityPlacementRf3OwnerLossHeld.LostReplyAsync(await original);
        }, failures);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await TenantCapacityPlacementRf3OwnerLossLifetime.JoinAsync(wave, owner, controls, arm,
            discovery, original, marker, held, caller, observer, failures, cleanup.Token);
        if (failures.Count != 0)
        {
            wave.RetainRoots();
            controls.RetainEvidence();
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var observed = held ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        await TenantCapacityPlacementRf3SecondOwnerLossCold.RequireAsync(wave, seeds, moves, observed);
        await controls.RetireArmAsync(arm, token);
        return await TenantCapacityPlacementRf3SecondOwnerLossRecovery.RunAsync(
            wave, seeds, state, moves, pending, id, request, observed, token);
    }
}
