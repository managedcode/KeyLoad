using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLoss
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 plan,
        PlacementIntentV1 pending, Guid persistId, PersistPlacementIntentRequest persist, CancellationToken token)
    {
        var move = plan.Plan.Moves.Single();
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            pending.IntentId, pending.Revision, state.Control);
        var identity = new TenantCapacityPlacementRf3OwnerLossHeld.Identity(Guid.NewGuid(), request,
            persistId, persist, pending);
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var phase = PartitionMovementParentPhaseIds.For(move, pending.PrincipalId, PartitionMovementParentPhaseRole.Prepare);
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(pending.PrincipalId, phase, null, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeAction.Hold, move.Partition);
        var originalOwner = wave.Application;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var observer = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = seed.Source.ApplyPlacementIntentAsync(identity.ApplyId, request, caller.Token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery, observer.Token);
        TenantCapacityPlacementRf3OwnerLossHeld.State? held = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            held = await TenantCapacityPlacementRf3OwnerLossHeld.CaptureAsync(seed, pending, move, identity.ApplyId,
                request, controls, discovery, original, marker, token);
            await PhysicalShardPartitionMovementRf3FaultOwner.KillAndJoinAsync(
                wave, TwoRf3MembershipProtocol.Node1, token);
            await TenantCapacityPlacementRf3OwnerLossHeld.LostReplyAsync(await original);
        }, failures);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await TenantCapacityPlacementRf3OwnerLossLifetime.JoinAsync(wave, originalOwner, controls, arm,
            discovery, original, marker, held, caller, observer, failures, cleanup.Token);
        if (failures.Count != 0)
        {
            wave.RetainRoots();
            controls.RetainEvidence();
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var observed = held ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        await TenantCapacityPlacementRf3OwnerLossCold.RequireAsync(wave, seed, move, observed);
        await controls.RetireArmAsync(arm, token);
        await TenantCapacityPlacementRf3OwnerLossRecovery.RunAsync(wave, seed, state, move, identity, observed, token);
    }
}
