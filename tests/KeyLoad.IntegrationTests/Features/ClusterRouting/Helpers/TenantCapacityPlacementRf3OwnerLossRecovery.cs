using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLossRecovery
{
    private const long ReclaimAndComplete = 2;
    private const int OneMove = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest move,
        TenantCapacityPlacementRf3OwnerLossHeld.Identity identity, TenantCapacityPlacementRf3OwnerLossHeld.State held,
        CancellationToken token)
    {
        var id = identity.ApplyId;
        var request = identity.Apply;
        var pending = identity.Pending;
        await wave.RestartJoinedAsync(token);
        await wave.WaitForSixHealthyAsync(token);
        await seed.ReconnectAsync(wave, token);
        var current = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        await FreshOwnerAsync(held, current);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        await Assert.That(held.Applying.Claim!.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var completed = (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value!;
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(completed.Revision).IsEqualTo(checked(held.Applying.Revision + ReclaimAndComplete));
        await Assert.That(completed.CompletedMoves).IsEqualTo(OneMove);
        await Assert.That(completed.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(completed.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(completed.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
        await Assert.That(completed.PrincipalId).IsEqualTo(pending.PrincipalId);
        await SqlRf3Protocol.EqualAsync(pending.Plan, completed.Plan);
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, completed.ControlOwner);
        var claim = completed.Claim ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        var previous = held.Applying.Claim ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        await Assert.That(claim.Nonce).IsNotEqualTo(previous.Nonce);
        await Assert.That(claim.SiloAddress).IsNotEqualTo(previous.SiloAddress);
        var claimed = current.Single(value => value.VoterId == claim.VoterId);
        await Assert.That(claim.SiloAddress).IsEqualTo(claimed.SiloAddress);
        await Assert.That(claim.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(current.Select(value => value.SiloAddress)));
        await Assert.That(claim.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var receipt = completed.Receipts.Single();
        await Assert.That(receipt.MoveId).IsEqualTo(move.MoveId);
        await Assert.That(receipt.Partition).IsEqualTo(move.Partition);
        await Assert.That(receipt.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await TenantCapacityPlacementRf3OwnerLossReplay.RequireAsync(seed, state, move, identity, completed, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await seed.Source.ApplyPlacementIntentAsync(
            id, request with
            {
                ExpectedRevision = completed.Revision
            }, token), ErrorCode.Conflict);
        await TenantCapacityPlacementRf3OwnerLossReplay.RequireAsync(seed, state, move, identity, completed, token);
        await TenantCapacityPlacementRf3OwnerLossCold.HealthyAsync(wave, seed, state, move, identity, completed, token);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }

    private static async Task FreshOwnerAsync(TenantCapacityPlacementRf3OwnerLossHeld.State held,
        ReplicaSiloDiscovery[] current)
    {
        await Assert.That(current.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var before in held.Discovery)
        {
            var after = current.Single(value => value.VoterId == before.VoterId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ClusterId).IsEqualTo(before.ClusterId);
            await Assert.That(current.Any(value => value.SiloAddress == before.SiloAddress)).IsFalse();
        }
        var fingerprint = TwoRf3MembershipFingerprintAssertions.Fingerprint(current.Select(value => value.SiloAddress));
        await Assert.That(fingerprint).IsNotEqualTo(held.Applying.Claim!.CohortFingerprint);
        await Assert.That(current.Any(value => value.SiloAddress == held.Applying.Claim.SiloAddress)).IsFalse();
    }
}
