using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiHold
{
    private const int FirstMove = 0;
    private const int HeldMove = 1;
    internal static async Task<PlacementIntentV1> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest[] moves, PlacementIntentV1 pending, Guid id,
        ApplyPlacementIntentRequest request, CancellationToken token)
    {
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var move = moves[HeldMove];
        var phase = PartitionMovementParentPhaseIds.For(move, pending.PrincipalId,
            PartitionMovementParentPhaseRole.Prepare);
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(pending.PrincipalId, phase, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold, move.Partition);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var markerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = seeds[FirstMove].Source.ApplyPlacementIntentAsync(id, request, cancellation.Token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery, markerCancellation.Token);
        var failures = new List<Exception>();
        PlacementIntentV1? completed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            completed = await ObserveAsync(seeds, state, moves, pending,
                id, request, controls, discovery, original, marker, token);
        }, failures);
        if (failures.Count != 0)
        {
            wave.RetainRoots();
            controls.RetainEvidence();
        }
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await TenantCapacityPlacementRf3ClaimLifetime.JoinAsync(controls, arm, discovery,
            original, marker, cancellation, markerCancellation, failures, cleanup.Token);
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    private static async Task<PlacementIntentV1> ObserveAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest[] moves,
        PlacementIntentV1 pending, Guid id, ApplyPlacementIntentRequest request,
        RequestCqrsProbeFixture controls, ReplicaSiloDiscovery[] discovery,
        Task<Result<PlacementIntentV1>> original, Task<RequestCqrsProbeMarkerRecord> markerTask, CancellationToken token)
    {
        if (await Task.WhenAny(original, markerTask) == original)
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await original);
            throw new InvalidOperationException("The original Apply completed before its second committed Prepare hold.");
        }
        var marker = await markerTask;
        var proof = RequestCqrsMovementJournalReader.Read(controls, marker);
        var applying = await TenantCapacityPlacementRf3MultiHeldAssertions.ReadAsync(seeds[FirstMove], request, token);
        await TenantCapacityPlacementRf3MultiHeldAssertions.RequireAsync(
            pending, applying, moves, id, request, marker, proof, discovery);
        await TenantCapacityPlacementRf3MultiAssertions.PlacementsAsync(seeds[FirstMove], state, moves, HeldMove, token);
        var first = seeds.Single(value => value.Partition == moves[FirstMove].Partition);
        await PartitionMovementPublicCallerReplay.RequireAsync(first.Source, first.Official,
            moves[FirstMove], applying.Receipts.Single(), token);
        await Assert.That(original.IsCompleted).IsFalse();
        await SqlRf3Protocol.EqualAsync(applying,
            await TenantCapacityPlacementRf3MultiHeldAssertions.ReadAsync(seeds[FirstMove], request, token));
        await SqlRf3Protocol.EqualAsync(proof, RequestCqrsMovementJournalReader.Read(controls, marker));
        controls.WriteRelease(marker.ArmId, marker.RequestId);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await original);
        await SqlRf3Protocol.EqualAsync(applying.Claim, completed.Claim);
        return completed;
    }
}
