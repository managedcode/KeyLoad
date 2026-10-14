using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3FailureDomainsAssertions
{
    private const string McpStaleDetail = "The database operation could not be completed.";
    internal static async Task<PlacementIntentV1> RefuseAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3FailureDomainsOperation.Original original,
        long expectedRevision, CancellationToken token)
    {
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var result = await seed.Source.ApplyPlacementIntentAsync(original.ApplyId, original.Apply, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(result, ErrorCode.Conflict);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale), result.Problem!);
        var applying = await ReadAsync(seed, original, token);
        await IdentityAsync(original, applying, expectedRevision);
        var claim = applying.Claim!;
        var owner = discovery.Single(value => value.VoterId == claim.VoterId);
        await Assert.That(owner.SiloAddress).IsEqualTo(claim.SiloAddress);
        await Assert.That(owner.Incarnation).IsEqualTo(original.Pending.ControlOwner.Incarnation);
        await Assert.That(original.Pending.ControlOwner.VoterIds.Contains(claim.VoterId, StringComparer.Ordinal)).IsTrue();
        await Assert.That(claim.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(discovery.Select(value => value.SiloAddress)));
        await Assert.That(claim.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
            new
            {
                commandId = original.ApplyId,
                request = original.Apply
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpStaleDetail);
        await SqlRf3Protocol.EqualAsync(applying, await ReadAsync(seed, original, token));
        return applying;
    }

    private static async Task IdentityAsync(TenantCapacityPlacementRf3FailureDomainsOperation.Original original,
        PlacementIntentV1 actual, long revision)
    {
        await Assert.That(actual.Status).IsEqualTo(PlacementIntentStatus.Applying);
        await Assert.That(actual.Revision).IsEqualTo(revision);
        await Assert.That(actual.OriginalApplyCommandId).IsEqualTo(original.ApplyId);
        await Assert.That(actual.OriginalApplyRequest).IsEqualTo(original.Apply);
        await Assert.That(actual.Claim).IsNotNull();
        await Assert.That(actual.Claim!.Nonce).IsNotEqualTo(Guid.Empty);
        await SqlRf3Protocol.EqualAsync(original.Pending with
        {
            Status = PlacementIntentStatus.Applying,
            Revision = revision,
            OriginalApplyCommandId = original.ApplyId,
            OriginalApplyRequest = original.Apply,
            Claim = actual.Claim
        }, actual);
    }

    internal static async Task<PlacementIntentV1> ReadAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3FailureDomainsOperation.Original original, CancellationToken token)
    {
        var request = new PlacementIntentReadRequest(new(original.Apply.TenantId,
            original.Apply.DatabaseId, original.Apply.ControlPartition), original.Pending.IntentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(request, token));
        await SqlRf3Protocol.EqualAsync(sdk, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool, new
            {
                request
            }, token))).Value);
        return sdk;
    }
}
