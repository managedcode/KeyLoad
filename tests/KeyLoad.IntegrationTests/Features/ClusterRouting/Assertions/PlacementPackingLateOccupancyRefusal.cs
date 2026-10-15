using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PlacementPackingLateOccupancyRefusal
{
    private const string SafeDetail = "The database operation could not be completed.";

    internal static async Task<PlacementIntentV1> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3PendingPolicyOperation.Original original,
        long revision, Func<Task> current, CancellationToken token)
    {
        var sdk = await seed.Source.ApplyPlacementIntentAsync(original.ApplyId, original.Apply, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Conflict);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale), sdk.Problem!);
        await current();
        var applying = await PlacementPackingLateOccupancyCurrent.ReadAsync(seed, original, current, token);
        await RequireAsync(wave, original, applying, revision, token);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
            new
            {
                commandId = original.ApplyId,
                request = original.Apply
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(SafeDetail);
        await current();
        await SqlRf3Protocol.EqualAsync(applying,
            await PlacementPackingLateOccupancyCurrent.ReadAsync(seed, original, current, token));
        return applying;
    }

    private static async Task RequireAsync(TwoRf3MembershipWave wave,
        TenantCapacityPlacementRf3PendingPolicyOperation.Original original, PlacementIntentV1 actual,
        long revision, CancellationToken token)
    {
        await Assert.That(actual.Claim).IsNotNull();
        var claim = actual.Claim!;
        await Assert.That(claim.Nonce).IsNotEqualTo(Guid.Empty);
        var members = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var owner = members.Single(value => value.VoterId == claim.VoterId);
        await Assert.That(owner.SiloAddress).IsEqualTo(claim.SiloAddress);
        await Assert.That(owner.Incarnation).IsEqualTo(original.Pending.ControlOwner.Incarnation);
        await Assert.That(original.Pending.ControlOwner.VoterIds.Contains(claim.VoterId, StringComparer.Ordinal)).IsTrue();
        await Assert.That(claim.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(members.Select(value => value.SiloAddress)));
        await Assert.That(claim.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await SqlRf3Protocol.EqualAsync(original.Pending with
        {
            Status = PlacementIntentStatus.Applying,
            Revision = revision,
            Claim = claim,
            OriginalApplyCommandId = original.ApplyId,
            OriginalApplyRequest = original.Apply
        }, actual);
    }
}
