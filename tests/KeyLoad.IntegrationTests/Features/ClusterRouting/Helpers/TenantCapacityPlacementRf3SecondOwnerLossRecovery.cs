using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3SecondOwnerLossRecovery
{
    private const int First = 0;
    private const int AllCompleted = 2;
    private const int CompletedFirst = 1;
    private const long ReclaimAndComplete = 2;

    internal static async Task<PlacementIntentV1> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest[] moves, PlacementIntentV1 pending, Guid id,
        ApplyPlacementIntentRequest request, TenantCapacityPlacementRf3OwnerLossHeld.State held, CancellationToken token)
    {
        await wave.RestartJoinedAsync(token);
        await wave.WaitForSixHealthyAsync(token);
        foreach (var seed in seeds)
        { await seed.ReconnectAsync(wave, token); }
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        await FreshOwnerAsync(held, discovery);
        await TenantCapacityPlacementRf3MultiAssertions.PlacementsAsync(seeds[First], state, moves, CompletedFirst, token);
        await Assert.That(held.Applying.Claim!.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var completed = (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seeds[First].Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
                new { commandId = id, request }, token))).Value;
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(completed.Revision).IsEqualTo(checked(held.Applying.Revision + ReclaimAndComplete));
        await Assert.That(completed.CompletedMoves).IsEqualTo(AllCompleted);
        await Assert.That(completed.Receipts.Length).IsEqualTo(AllCompleted);
        await Assert.That(completed.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(completed.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(completed.PrincipalId).IsEqualTo(pending.PrincipalId);
        await Assert.That(completed.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
        await SqlRf3Protocol.EqualAsync(pending.Plan, completed.Plan);
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, completed.ControlOwner);
        await SqlRf3Protocol.EqualAsync(held.Applying.Receipts[First], completed.Receipts[First]);
        await ClaimAsync(held, completed, discovery);
        await TenantCapacityPlacementRf3MultiReplay.RequireAsync(seeds, state, moves, id, request, completed, token);
        return completed;
    }

    private static async Task FreshOwnerAsync(TenantCapacityPlacementRf3OwnerLossHeld.State held,
        ReplicaSiloDiscovery[] current)
    {
        await Assert.That(current.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var previous in held.Discovery)
        {
            var next = current.Single(value => value.VoterId == previous.VoterId);
            await Assert.That(next.Incarnation).IsEqualTo(previous.Incarnation);
            await Assert.That(next.ClusterId).IsEqualTo(previous.ClusterId);
            await Assert.That(current.Any(value => value.SiloAddress == previous.SiloAddress)).IsFalse();
        }
        await Assert.That(current.Any(value => value.SiloAddress == held.Applying.Claim!.SiloAddress)).IsFalse();
        await Assert.That(TwoRf3MembershipFingerprintAssertions.Fingerprint(current.Select(value => value.SiloAddress)))
            .IsNotEqualTo(held.Applying.Claim!.CohortFingerprint);
    }

    private static async Task ClaimAsync(TenantCapacityPlacementRf3OwnerLossHeld.State held,
        PlacementIntentV1 completed, ReplicaSiloDiscovery[] discovery)
    {
        var previous = held.Applying.Claim ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        var next = completed.Claim ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        await Assert.That(next.Nonce).IsNotEqualTo(previous.Nonce);
        await Assert.That(next.SiloAddress).IsNotEqualTo(previous.SiloAddress);
        var voter = discovery.Single(value => value.VoterId == next.VoterId);
        await Assert.That(next.SiloAddress).IsEqualTo(voter.SiloAddress);
        await Assert.That(completed.ControlOwner.VoterIds.Contains(next.VoterId, StringComparer.Ordinal)).IsTrue();
        await Assert.That(voter.Incarnation).IsEqualTo(completed.ControlOwner.Incarnation);
        await Assert.That(next.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(discovery.Select(value => value.SiloAddress)));
        await Assert.That(next.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
    }
}
