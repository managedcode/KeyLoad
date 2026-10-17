using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PlacementIntentExecutor
{
    private const int CompleteCohort = 6;

    private async Task<PlacementIntentV1> ClaimAsync(GrainRequestEnvelope trusted, string principalId,
        PlacementIntentV1 original, ApplyPlacementIntentRequest originalRequest, ReadExecutionBudget work, CancellationToken token)
    {
        var membership = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        var local = node.Discovery?.Read();
        if (membership is null || membership.ActiveSilos != CompleteCohort || local is null || !local.TransportReady
            || !original.ControlOwner.VoterIds.Contains(local.VoterId, StringComparer.Ordinal)
            || original.ControlOwner.Incarnation != local.Incarnation || trusted.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort);
        }
        var absent = original.Claim is { } previous
            && !node.ReadActiveNativeMembership().Any(item => item.SiloAddress == previous.SiloAddress);
        var claim = new PlacementIntentClaimV1(local.VoterId, local.SiloAddress,
            membership.ActiveFingerprint, Guid.NewGuid(), trusted.ExpiresAt);
        var phase = new PlacementIntentPhaseRequestV1(TenantPlacementPolicyProtocol.Version,
            original.Plan.TenantId, original.Plan.DatabaseId, original.IntentId, original.Revision,
            original.OriginalApplyCommandId ?? trusted.CommandId, PlacementIntentStatus.Applying,
            claim, null, absent, original.Plan.ControlPartition, original.OriginalApplyRequest ?? originalRequest);
        return await phases.ExecuteAsync(principalId, phase, trusted.ExpiresAt, work, token).ConfigureAwait(false);
    }
}
