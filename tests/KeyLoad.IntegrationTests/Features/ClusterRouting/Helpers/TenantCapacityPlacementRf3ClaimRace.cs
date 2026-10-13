using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ClaimRace
{
    internal static async Task<PlacementIntentV1> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantPlacementPlanResultV1 plan,
        PlacementIntentV1 pending, Guid id, ApplyPlacementIntentRequest request, CancellationToken token)
    {
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var move = plan.Plan.Moves.Single();
        var phase = PartitionMovementParentPhaseIds.For(move,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.Prepare);
        var controls = wave.QueryControls;
        var arm = controls.WriteArm(pending.PrincipalId, phase, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold, move.Partition);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var markerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = seed.Source.ApplyPlacementIntentAsync(id, request, cancellation.Token);
        var marker = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery, markerCancellation.Token);
        var failures = new List<Exception>();
        PlacementIntentV1? completed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        { completed = await ObserveHeldAsync(seed, pending, move, id, request, controls, discovery, original, marker, token); }, failures);
        if (failures.Count != 0)
        { wave.RetainRoots(); controls.RetainEvidence(); }
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await TenantCapacityPlacementRf3ClaimLifetime.JoinAsync(controls, arm, discovery,
            original, marker, cancellation, markerCancellation, failures, cleanup.Token);
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    private static async Task<PlacementIntentV1> ObserveHeldAsync(PartitionMovementPublicParentRf3Seed seed,
        PlacementIntentV1 pending, PartitionMoveRequest move, Guid id, ApplyPlacementIntentRequest request,
        RequestCqrsProbeFixture controls, ReplicaSiloDiscovery[] discovery,
        Task<Result<PlacementIntentV1>> original, Task<RequestCqrsProbeMarkerRecord> markerTask, CancellationToken token)
    {
        if (await Task.WhenAny(original, markerTask) == original)
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await original);
            throw new InvalidOperationException("The original Apply completed before its committed Prepare hold.");
        }
        var marker = await markerTask;
        var proof = RequestCqrsMovementJournalReader.Read(controls, marker);
        var applying = await ReadAsync(seed, request, token);
        await TenantCapacityPlacementRf3ClaimAssertions.ApplyingAsync(
            pending, applying, move, id, request, marker, proof, discovery);
        await Assert.That(original.IsCompleted).IsFalse();
        var refusal = await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
            new { commandId = id, request }, token);
        await McpCallerAssertions.ErrorAsync(refusal, ErrorCode.Conflict, dispatched: true);
        await Assert.That(refusal.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(
                KeyLoad.Core.Features.ClusterRouting.Contracts.TenantPlacementPolicyProtocol.Stale);
        await SqlRf3Protocol.EqualAsync(applying, await ReadAsync(seed, request, token));
        await SqlRf3Protocol.EqualAsync(proof, RequestCqrsMovementJournalReader.Read(controls, marker));
        await Assert.That(original.IsCompleted).IsFalse();
        controls.WriteRelease(marker.ArmId, marker.RequestId);
        return await McpCallerAssertions.SdkSuccessAsync(await original);
    }

    private static async Task<PlacementIntentV1> ReadAsync(PartitionMovementPublicParentRf3Seed seed,
        ApplyPlacementIntentRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(
            new(new(request.TenantId, request.DatabaseId, request.ControlPartition), request.IntentId), token));
}
